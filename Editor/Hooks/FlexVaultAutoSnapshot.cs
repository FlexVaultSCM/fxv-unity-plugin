using System.IO;
using FlexVault.VCS.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.Hooks
{
    /// <summary>
    /// Fires a best-effort local "fxv snapshot" around specific high-entropy editor operations,
    /// not on every save. A single-object tweak followed by Ctrl+S doesn't get one; a prefab
    /// apply, a terrain save, or a scene save after a bulk delete/reparent does.
    /// </summary>
    [InitializeOnLoad]
    public class FlexVaultAutoSnapshot : AssetModificationProcessor
    {
        // One save gesture can trigger OnWillSaveAssets more than once in a row (e.g. prefab
        // stage auto-save then the outer scene save). Debounce so it's one snapshot, not a burst.
        private const double DebounceSeconds = 2.0;
        private static double s_lastSnapshotTime = -1;

        // A multi-select delete/reparent fires several ObjectChangeEvents in one published batch,
        // one per affected object. Below this count it's just ordinary single-object editing.
        private const int BulkStructuralChangeThreshold = 3;

        private static int s_pendingDestroyedCount;
        private static int s_pendingRestructuredCount;

        static FlexVaultAutoSnapshot()
        {
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
        }

        private static void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            int destroyedInBatch = 0;
            int restructuredInBatch = 0;

            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        destroyedInBatch++;
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        restructuredInBatch++;
                        break;
                }
            }

            // Accumulate rather than overwrite, so a couple of sub-threshold edits between saves
            // still add up to something worth flagging.
            if (destroyedInBatch >= BulkStructuralChangeThreshold)
            {
                s_pendingDestroyedCount += destroyedInBatch;
            }
            if (restructuredInBatch >= BulkStructuralChangeThreshold)
            {
                s_pendingRestructuredCount += restructuredInBatch;
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

            double now = EditorApplication.timeSinceStartup;
            if (s_lastSnapshotTime >= 0 && now - s_lastSnapshotTime < DebounceSeconds)
            {
                return;
            }
            s_lastSnapshotTime = now;
            s_pendingDestroyedCount = 0;
            s_pendingRestructuredCount = 0;

            // Fire-and-forget: OnWillSaveAssets has to return synchronously with the paths to
            // save, so we don't wait on the snapshot here.
            _ = FxvRunner.SnapshotAsync(description);
        }

        private static string BuildDescription(string[] paths)
        {
            foreach (string path in paths)
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".prefab")
                {
                    return $"Auto-snapshot before prefab save ({Path.GetFileName(path)})";
                }

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
                if (s_pendingDestroyedCount > 0)
                {
                    return $"Auto-snapshot before scene save (deleted {s_pendingDestroyedCount} objects)";
                }
                if (s_pendingRestructuredCount > 0)
                {
                    return $"Auto-snapshot before scene save (restructured {s_pendingRestructuredCount} objects)";
                }
            }

            return null;
        }
    }
}
