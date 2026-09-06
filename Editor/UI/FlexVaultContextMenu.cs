using System;
using System.Collections.Generic;
using FlexVault.VCS.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.UI
{
    public static class FlexVaultContextMenu
    {
        private const string MenuRoot = "Assets/FlexVault/";

        [MenuItem(MenuRoot + "Open FlexVault Window", false, 100)]
        public static void OpenWindow()
        {
            FlexVaultWindow.ShowWindow();
        }

        [MenuItem(MenuRoot + "Refresh Status", false, 101)]
        public static void RefreshStatus()
        {
            FlexVaultStateCache.RefreshAsync();
        }

        [MenuItem(MenuRoot + "Revert Selected", false, 120)]
        public static async void RevertSelected()
        {
            var selectedGuids = Selection.assetGUIDs;
            if (selectedGuids == null || selectedGuids.Length == 0)
            {
                EditorUtility.DisplayDialog("FlexVault", "No assets selected to revert.", "OK");
                return;
            }

            var projectPaths = new List<string>();
            foreach (string guid in selectedGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    projectPaths.Add(path);
                }
            }

            if (projectPaths.Count == 0)
            {
                return;
            }

            var expandedPaths = FlexVaultMetaHelper.ExpandWithMeta(projectPaths);
            var repoRelativePaths = new List<string>();
            foreach (string p in expandedPaths)
            {
                repoRelativePaths.Add(FlexVaultMetaHelper.ToRepoRelativePath(p));
            }

            string fileListStr = string.Join("\n", projectPaths);
            if (projectPaths.Count > 5)
            {
                fileListStr = $"{projectPaths.Count} files (including companion .meta files)";
            }

            if (!EditorUtility.DisplayDialog(
                "Confirm Revert",
                $"Are you sure you want to revert the following file(s) to the published base?\n\n{fileListStr}\n\nUnsaved working tree modifications will be lost.",
                "Revert",
                "Cancel"))
            {
                return;
            }

            if (!FlexVaultSafetyGuards.EnsureSafeToMutateWorkspace("Revert", promptSaveDirtyScenes: true))
            {
                return;
            }

            EditorApplication.LockReloadAssemblies();
            try
            {
                EditorUtility.DisplayProgressBar("FlexVault", "Reverting selected files...", 0.5f);
                var result = await FxvRunner.RevertAsync(repoRelativePaths);

                if (!result.Success)
                {
                    EditorUtility.DisplayDialog("Revert Failed", result.ErrorMessage, "OK");
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Revert Error", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorApplication.UnlockReloadAssemblies();
                FlexVaultStateCache.RefreshAsync();
            }
        }

        [MenuItem(MenuRoot + "Diff Selected Against Base", false, 121)]
        public static async void DiffSelected()
        {
            var selectedGuids = Selection.assetGUIDs;
            if (selectedGuids == null || selectedGuids.Length == 0)
            {
                return;
            }

            string projectPath = AssetDatabase.GUIDToAssetPath(selectedGuids[0]);
            if (string.IsNullOrEmpty(projectPath))
            {
                return;
            }

            string repoRelative = FlexVaultMetaHelper.ToRepoRelativePath(projectPath);
            await FlexVaultDiffHelper.DiffFileAgainstBaseAsync(repoRelative);
        }

        [MenuItem(MenuRoot + "Diff Selected Against Base", true)]
        public static bool ValidateDiffSelected()
        {
            return Selection.assetGUIDs != null && Selection.assetGUIDs.Length == 1 && FlexVaultSettings.IsInFlexVaultRepository();
        }

        [MenuItem(MenuRoot + "History", false, 140)]
        public static void ShowHistory()
        {
            string targetPath = null;
            if (Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0)
            {
                string projectPath = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
                if (!string.IsNullOrEmpty(projectPath))
                {
                    targetPath = FlexVaultMetaHelper.ToRepoRelativePath(projectPath);
                }
            }

            FlexVaultHistoryWindow.ShowHistory(targetPath);
        }

        [MenuItem(MenuRoot + "History", true)]
        public static bool ValidateShowHistory()
        {
            return FlexVaultSettings.IsInFlexVaultRepository();
        }

        [MenuItem(MenuRoot + "Resolve Conflict/Keep Mine (Local Draft)", false, 130)]
        public static void ResolveMineSelected()
        {
            ResolveSelectedConflict(FxvRunner.ResolveAction.Mine);
        }

        [MenuItem(MenuRoot + "Resolve Conflict/Take Theirs (Published)", false, 131)]
        public static void ResolveTheirsSelected()
        {
            ResolveSelectedConflict(FxvRunner.ResolveAction.Theirs);
        }

        private static async void ResolveSelectedConflict(FxvRunner.ResolveAction action)
        {
            var selectedGuids = Selection.assetGUIDs;
            if (selectedGuids == null || selectedGuids.Length == 0) return;

            var paths = new List<string>();
            foreach (string guid in selectedGuids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(p)) paths.Add(p);
            }
            if (paths.Count == 0) return;

            var expanded = FlexVaultMetaHelper.ExpandWithMeta(paths);
            var repoRelative = new List<string>();
            foreach (var p in expanded)
            {
                repoRelative.Add(FlexVaultMetaHelper.ToRepoRelativePath(p));
            }

            string actionName = action == FxvRunner.ResolveAction.Mine ? "Keep Mine" : "Take Theirs";
            if (!EditorUtility.DisplayDialog(
                "Confirm Conflict Resolution",
                $"Resolve {paths.Count} file(s) with action: {actionName}?\nThis will clear the conflict state.",
                "Resolve",
                "Cancel"))
            {
                return;
            }

            if (!FlexVaultSafetyGuards.EnsureSafeToMutateWorkspace("Resolve Conflicts", promptSaveDirtyScenes: true))
            {
                return;
            }

            EditorApplication.LockReloadAssemblies();
            try
            {
                EditorUtility.DisplayProgressBar("FlexVault", $"Resolving conflicts ({actionName})...", 0.5f);
                var result = await FxvRunner.ResolveAsync(action, repoRelative);
                if (!result.Success)
                {
                    EditorUtility.DisplayDialog("Resolve Failed", result.ErrorMessage, "OK");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorApplication.UnlockReloadAssemblies();
                FlexVaultStateCache.RefreshAsync();
            }
        }

        [MenuItem(MenuRoot + "Resolve Conflict/Keep Mine (Local Draft)", true)]
        [MenuItem(MenuRoot + "Resolve Conflict/Take Theirs (Published)", true)]
        public static bool ValidateResolveConflict()
        {
            return Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0 && FlexVaultSettings.IsInFlexVaultRepository();
        }

        [MenuItem(MenuRoot + "Revert Selected", true)]
        public static bool ValidateRevertSelected()
        {
            return Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0 && FlexVaultSettings.IsInFlexVaultRepository();
        }

        [MenuItem(MenuRoot + "Refresh Status", true)]
        public static bool ValidateRefreshStatus()
        {
            return FlexVaultSettings.IsInFlexVaultRepository();
        }
    }
}
