using System;
using FlexVault.VCS.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.Hooks
{
    public class FlexVaultPostprocessor : AssetPostprocessor
    {
        private const double DebounceSeconds = 0.3;
        private static double s_lastChangeTime = -1;
        private static bool s_isScheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!FlexVaultSettings.IsInFlexVaultRepository())
            {
                return;
            }

            int changeCount = importedAssets.Length + deletedAssets.Length + movedAssets.Length + movedFromAssetPaths.Length;
            if (changeCount > 0)
            {
                TriggerDebouncedRefresh();
            }
        }

        public static void TriggerDebouncedRefresh()
        {
            s_lastChangeTime = EditorApplication.timeSinceStartup;

            if (!s_isScheduled)
            {
                s_isScheduled = true;
                EditorApplication.update += OnEditorUpdate;
            }
        }

        private static void OnEditorUpdate()
        {
            if (s_lastChangeTime < 0)
            {
                EditorApplication.update -= OnEditorUpdate;
                s_isScheduled = false;
                return;
            }

            double elapsed = EditorApplication.timeSinceStartup - s_lastChangeTime;
            if (elapsed >= DebounceSeconds)
            {
                EditorApplication.update -= OnEditorUpdate;
                s_isScheduled = false;
                s_lastChangeTime = -1;

                FlexVaultStateCache.RefreshAsync(skipScan: true);
            }
        }
    }
}
