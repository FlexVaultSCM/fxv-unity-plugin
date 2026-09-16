using System.Collections.Generic;
using System.IO;
using FlexVault.VCS.Editor.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlexVault.VCS.Editor.Hooks
{
    /// <summary>
    /// Fires a best-effort local "fxv snapshot" around specific high-entropy editor operations,
    /// not on every save. A single-object tweak followed by Ctrl+S doesn't get one; a terrain
    /// save, a scene save after a bulk delete/reparent, a prefab unpack, or a large asset
    /// reimport does. Ordinary prefab saves are deliberately not snapshotted - they happen too
    /// often (e.g. repeatedly while editing in Prefab Mode) to be a useful checkpoint signal.
    /// </summary>
    [InitializeOnLoad]
    public class FlexVaultAutoSnapshot : AssetModificationProcessor
    {
        // One trigger firing shouldn't spawn a burst of snapshots for what's really one gesture
        // (e.g. prefab stage auto-save then the outer scene save, or unpack immediately followed
        // by a save).
        private const double DebounceSeconds = 2.0;
        private static double s_lastSnapshotTime = -1;

        // Periodic snapshot: if pending changes have sat for longer than the configured interval
        // with no snapshot of any kind (hook-triggered or manual), take one automatically. Checked
        // on a coarse timer rather than every EditorApplication.update tick, since evaluating it
        // involves copying the current change list.
        private const double PeriodicCheckIntervalSeconds = 5.0;
        private static double s_lastPeriodicCheckTime = -1;

        // A multi-select delete/reparent fires several ObjectChangeEvents in one published batch,
        // one per affected object. Below this count it's just ordinary single-object editing.
        private const int BulkStructuralChangeThreshold = 3;

        private static int s_pendingDestroyedCount;
        private static int s_pendingRestructuredCount;

        // Roots of currently-connected prefab instances in open scenes, so a structural change
        // event can be recognized as "this GameObject just lost its prefab connection" (an
        // unpack) rather than ordinary editing.
        private static readonly HashSet<int> s_knownPrefabInstanceRootIds = new HashSet<int>();

        static FlexVaultAutoSnapshot()
        {
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
            EditorSceneManager.sceneOpened += (scene, mode) => RefreshPrefabInstanceRoots();
            EditorSceneManager.sceneSaved += scene => RefreshPrefabInstanceRoots();
            // Scenes aren't guaranteed to be loaded yet at static-constructor time.
            EditorApplication.delayCall += RefreshPrefabInstanceRoots;

            // Count the periodic interval from editor/domain-reload time, not from "never" - a
            // fresh session with old pending changes shouldn't fire a snapshot on the first tick.
            s_lastSnapshotTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += CheckPeriodicSnapshot;
        }

        private static void CheckPeriodicSnapshot()
        {
            double now = EditorApplication.timeSinceStartup;
            if (s_lastPeriodicCheckTime >= 0 && now - s_lastPeriodicCheckTime < PeriodicCheckIntervalSeconds)
            {
                return;
            }
            s_lastPeriodicCheckTime = now;

            if (!FlexVaultSettings.IsFlexVaultActive())
            {
                return;
            }

            int intervalSeconds = FlexVaultSettings.PeriodicSnapshotIntervalSeconds;
            if (intervalSeconds <= 0)
            {
                // Periodic snapshots disabled.
                return;
            }

            if (s_lastSnapshotTime >= 0 && now - s_lastSnapshotTime < intervalSeconds)
            {
                return;
            }

            var pendingChanges = FlexVaultStateCache.GetWorkspaceChanges();
            if (pendingChanges.Count == 0)
            {
                // Nothing to snapshot; don't burn a checkpoint on a clean workspace.
                return;
            }

            TriggerSnapshotDirect(BuildPeriodicDescription(pendingChanges));
        }

        // A bare "Periodic auto-snapshot" tells a developer nothing when they're scanning history
        // later - summarize what actually changed so periodic checkpoints stay as useful as the
        // hand-triggered ones above.
        private static string BuildPeriodicDescription(List<FileStatusItem> pendingChanges)
        {
            int added = 0, modified = 0, deleted = 0, conflicted = 0;
            foreach (var file in pendingChanges)
            {
                switch (file.EffectiveWorkspaceState?.ToLowerInvariant())
                {
                    case "added": added++; break;
                    case "modified": modified++; break;
                    case "deleted": deleted++; break;
                    case "conflicted": conflicted++; break;
                }
            }

            var parts = new List<string>();
            if (added > 0) parts.Add($"{added} added");
            if (modified > 0) parts.Add($"{modified} modified");
            if (deleted > 0) parts.Add($"{deleted} deleted");
            if (conflicted > 0) parts.Add($"{conflicted} conflicted");

            string summary = parts.Count > 0 ? string.Join(", ", parts) : $"{pendingChanges.Count} file(s) changed";
            return $"Periodic auto-snapshot ({summary})";
        }

        private static void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            int destroyedInBatch = 0;
            int restructuredInBatch = 0;
            List<GameObject> unpackedRoots = null;

            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        destroyedInBatch++;
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        restructuredInBatch++;
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out var structureData);
                        CheckForPrefabUnpack(structureData.instanceId, ref unpackedRoots);
                        break;
                }
            }

            // Accumulate every batch, even sub-threshold ones, so a couple of small edits between
            // saves still add up to something worth flagging; the threshold is applied once, against
            // the running total, when a save actually consumes it (see BuildDescription).
            if (destroyedInBatch > 0)
            {
                s_pendingDestroyedCount += destroyedInBatch;
            }
            if (restructuredInBatch > 0)
            {
                s_pendingRestructuredCount += restructuredInBatch;
            }

            if (unpackedRoots != null)
            {
                foreach (GameObject root in unpackedRoots)
                {
                    TriggerSnapshotDirect($"Auto-snapshot after prefab unpack ({root.name})");
                }
                // The unpacked roots are no longer prefab instances - drop them from tracking.
                RefreshPrefabInstanceRoots();
            }
        }

        private static void CheckForPrefabUnpack(int instanceId, ref List<GameObject> unpackedRoots)
        {
            if (!s_knownPrefabInstanceRootIds.Contains(instanceId))
            {
                return;
            }

#if UNITY_6000_0_OR_NEWER
            GameObject go = EditorUtility.EntityIdToObject(instanceId) as GameObject;
#else
            GameObject go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
#endif
            if (go == null || PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.Connected)
            {
                // Still connected (or already gone) - not an unpack.
                return;
            }

            if (unpackedRoots == null)
            {
                unpackedRoots = new List<GameObject>();
            }
            unpackedRoots.Add(go);
        }

        private static void RefreshPrefabInstanceRoots()
        {
            s_knownPrefabInstanceRootIds.Clear();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    CollectPrefabInstanceRoots(root);
                }
            }
        }

        private static void CollectPrefabInstanceRoots(GameObject go)
        {
            if (PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.Connected
                && PrefabUtility.GetOutermostPrefabInstanceRoot(go) == go)
            {
                s_knownPrefabInstanceRootIds.Add(go.GetInstanceID());
            }

            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                CollectPrefabInstanceRoots(t.GetChild(i).gameObject);
            }
        }

        private static string[] OnWillSaveAssets(string[] paths)
        {
            TriggerSnapshotIfWarranted(paths);
            return paths;
        }

        private static void TriggerSnapshotIfWarranted(string[] paths)
        {
            if (paths == null || paths.Length == 0 || !FlexVaultSettings.IsFlexVaultActive())
            {
                return;
            }

            string description = BuildDescription(paths);
            if (description == null)
            {
                // Ordinary save, nothing risky detected - deliberately not snapshotted.
                return;
            }

            if (TriggerSnapshotDirect(description))
            {
                s_pendingDestroyedCount = 0;
                s_pendingRestructuredCount = 0;
            }
        }

        // Called by any snapshot path outside this class (e.g. a manual Publish in the window)
        // so the periodic timer counts from the most recent snapshot of any kind, not just the
        // ones this class triggered itself.
        internal static void NotifySnapshotOccurred()
        {
            s_lastSnapshotTime = EditorApplication.timeSinceStartup;
        }

        // Shared entry point for every trigger, including ones not tied to a save (unpack,
        // reimport). Returns false if swallowed by the debounce.
        internal static bool TriggerSnapshotDirect(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                // A description-less auto-snapshot is useless in history later - every call site
                // must supply one, or not snapshot at all.
                return false;
            }

            if (!FlexVaultSettings.IsFlexVaultActive())
            {
                return false;
            }

            double now = EditorApplication.timeSinceStartup;
            if (s_lastSnapshotTime >= 0 && now - s_lastSnapshotTime < DebounceSeconds)
            {
                return false;
            }
            s_lastSnapshotTime = now;

            Debug.Log($"[FlexVault] {description}");

            // Fire-and-forget: callers here are event handlers, not places we can block on I/O.
            _ = FxvRunner.SnapshotAsync(description);
            return true;
        }

        private static string BuildDescription(string[] paths)
        {
            foreach (string path in paths)
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".asset" && AssetDatabase.LoadMainAssetAtPath(path) is TerrainData)
                {
                    return $"Auto-snapshot before terrain data save ({Path.GetFileName(path)})";
                }
            }

            bool isSceneSave = false;
            foreach (string path in paths)
            {
                if (Path.GetExtension(path).ToLowerInvariant() == ".unity")
                {
                    isSceneSave = true;
                    break;
                }
            }

            if (isSceneSave)
            {
                if (s_pendingDestroyedCount >= BulkStructuralChangeThreshold)
                {
                    return $"Auto-snapshot before scene save (deleted {s_pendingDestroyedCount} objects)";
                }
                if (s_pendingRestructuredCount >= BulkStructuralChangeThreshold)
                {
                    return $"Auto-snapshot before scene save (restructured {s_pendingRestructuredCount} objects)";
                }
            }

            return null;
        }
    }

    // Godot's plugin has the same post-hoc bulk-reimport gate; kept as a separate top-level class
    // here since OnPostprocessAllAssets requires deriving directly from AssetPostprocessor.
    internal class FlexVaultReimportWatcher : AssetPostprocessor
    {
        // Importing a handful of assets after a normal edit isn't worth a checkpoint; a big
        // batch (VCS sync, platform switch, asset store import) is.
        private const int BulkReimportThreshold = 20;

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            // Asset deletion doesn't go through Unity's Undo stack, so even one deleted asset is
            // worth a checkpoint - unlike reimports, there's no "small batch, not worth it" case.
            if (deletedAssets.Length > 0)
            {
                FlexVaultAutoSnapshot.TriggerSnapshotDirect($"Auto-snapshot after asset deletion ({deletedAssets.Length} assets)");
            }

            if (importedAssets.Length >= BulkReimportThreshold)
            {
                FlexVaultAutoSnapshot.TriggerSnapshotDirect($"Auto-snapshot after bulk reimport ({importedAssets.Length} assets)");
            }
        }
    }
}
