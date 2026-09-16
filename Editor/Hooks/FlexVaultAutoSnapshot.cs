using FlexVault.VCS.Editor.Core;
using UnityEditor;

namespace FlexVault.VCS.Editor.Hooks
{
    /// <summary>
    /// Fires a best-effort local "fxv snapshot" right before high-entropy editor operations that
    /// write asset/scene data to disk - prefab applies, prefab unpacks followed by a scene save,
    /// terrain edits, and ordinary scene/asset saves all funnel through
    /// AssetModificationProcessor.OnWillSaveAssets before Unity writes anything, so hooking it once
    /// here covers all of them without needing a separate hook per menu command.
    /// </summary>
    [InitializeOnLoad]
    public class FlexVaultAutoSnapshot : AssetModificationProcessor
    {
        // A single "Save" gesture (Ctrl+S, prefab stage auto-save, "Save Project") can trigger
        // OnWillSaveAssets more than once in quick succession. Debounce so one save gesture produces
        // one snapshot instead of a burst of overlapping fxv processes.
        private const double DebounceSeconds = 2.0;
        private static double s_lastSnapshotTime = -1;

        static FlexVaultAutoSnapshot()
        {
            EditorApplication.wantsToQuit += OnWantsToQuit;
        }

        private static string[] OnWillSaveAssets(string[] paths)
        {
            TriggerSnapshot(paths);
            return paths;
        }

        private static bool OnWantsToQuit()
        {
            if (FlexVaultSettings.IsFlexVaultActive())
            {
                // Best-effort last-chance snapshot; never blocks quitting on it.
                _ = FxvRunner.SnapshotAsync("Auto-snapshot before editor quit");
            }
            return true;
        }

        private static void TriggerSnapshot(string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return;
            }

            if (!FlexVaultSettings.IsFlexVaultActive())
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (s_lastSnapshotTime >= 0 && now - s_lastSnapshotTime < DebounceSeconds)
            {
                return;
            }
            s_lastSnapshotTime = now;

            string description = BuildDescription(paths);

            // Fire-and-forget: OnWillSaveAssets must return synchronously with the paths to save,
            // so we don't await the snapshot here. This is a best-effort safety checkpoint, not a
            // guarantee ordered against the save that follows it.
            _ = FxvRunner.SnapshotAsync(description);
        }

        private static string BuildDescription(string[] paths)
        {
            if (paths.Length == 1)
            {
                string path = paths[0];
                string fileName = System.IO.Path.GetFileName(path);
                string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();

                if (extension == ".prefab")
                {
                    return $"Auto-snapshot before prefab save ({fileName})";
                }
                if (extension == ".unity")
                {
                    return $"Auto-snapshot before scene save ({fileName})";
                }
                return $"Auto-snapshot before asset save ({fileName})";
            }

            return $"Auto-snapshot before saving {paths.Length} assets";
        }
    }
}
