using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.Core
{
    [InitializeOnLoad]
    public static class FlexVaultStateCache
    {
        private static readonly object s_lock = new object();
        private static readonly Dictionary<string, FileStatusItem> s_pathToStatus = new Dictionary<string, FileStatusItem>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> s_guidToState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static StatusPayload s_latestStatus;
        private static bool s_isRefreshing;
        private static double s_lastRefreshTime;

        public static event Action OnStateChanged;

        static FlexVaultStateCache()
        {
            EditorApplication.delayCall += () =>
            {
                if (FlexVaultSettings.IsInFlexVaultRepository())
                {
                    RefreshAsync();
                }
            };
        }

        public static bool IsRefreshing
        {
            get
            {
                lock (s_lock)
                {
                    return s_isRefreshing;
                }
            }
        }
        public static StatusPayload LatestStatus => s_latestStatus;

        public static string GetStateByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            lock (s_lock)
            {
                return s_guidToState.TryGetValue(guid, out string state) ? state : null;
            }
        }

        public static FileStatusItem GetStatusByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string repoRelative = FlexVaultMetaHelper.ToRepoRelativePath(path);
            lock (s_lock)
            {
                return s_pathToStatus.TryGetValue(repoRelative, out var item) ? item : null;
            }
        }

        public static List<FileStatusItem> GetChangedFiles()
        {
            lock (s_lock)
            {
                return s_latestStatus?.Files != null
                    ? new List<FileStatusItem>(s_latestStatus.Files)
                    : new List<FileStatusItem>();
            }
        }

        public static List<FileStatusItem> GetWorkspaceChanges()
        {
            lock (s_lock)
            {
                if (s_latestStatus?.Files == null) return new List<FileStatusItem>();
                var list = new List<FileStatusItem>();
                foreach (var f in s_latestStatus.Files)
                {
                    if (f.NeedsSnapshot) list.Add(f);
                }
                return list;
            }
        }

        public static List<FileStatusItem> GetUnpublishedChanges()
        {
            lock (s_lock)
            {
                if (s_latestStatus?.Files == null) return new List<FileStatusItem>();
                var list = new List<FileStatusItem>();
                foreach (var f in s_latestStatus.Files)
                {
                    if (f.IsUnpublished) list.Add(f);
                }
                return list;
            }
        }

        public static bool HasPendingChanges(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var item = GetStatusByPath(path);
            if (item != null && (item.NeedsSnapshot || item.IsConflicted))
            {
                return true;
            }

            string companionMeta = FlexVaultMetaHelper.GetCompanionMetaPath(path);
            if (!string.IsNullOrEmpty(companionMeta))
            {
                var metaItem = GetStatusByPath(companionMeta);
                if (metaItem != null && (metaItem.NeedsSnapshot || metaItem.IsConflicted))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasPendingChangesInFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return false;
            string repoRelative = FlexVaultMetaHelper.ToRepoRelativePath(folderPath);
            string prefix = FlexVaultMetaHelper.NormalizeSeparators(repoRelative).TrimEnd('/') + "/";

            lock (s_lock)
            {
                if (s_latestStatus?.Files == null) return false;
                foreach (var f in s_latestStatus.Files)
                {
                    if (f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        if (f.NeedsSnapshot || f.IsConflicted)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public static bool IsFileConflicted(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var item = GetStatusByPath(path);
            if (item != null && item.IsConflicted) return true;

            string companionMeta = FlexVaultMetaHelper.GetCompanionMetaPath(path);
            if (!string.IsNullOrEmpty(companionMeta))
            {
                var metaItem = GetStatusByPath(companionMeta);
                if (metaItem != null && metaItem.IsConflicted) return true;
            }

            return false;
        }

        public static bool HasConflictInFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return false;
            string repoRelative = FlexVaultMetaHelper.ToRepoRelativePath(folderPath);
            string prefix = FlexVaultMetaHelper.NormalizeSeparators(repoRelative).TrimEnd('/') + "/";

            lock (s_lock)
            {
                if (s_latestStatus?.Files == null) return false;
                foreach (var f in s_latestStatus.Files)
                {
                    if (f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && f.IsConflicted)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool s_refreshPending;

        public static async void RefreshAsync(bool skipScan = false)
        {
            lock (s_lock)
            {
                if (s_isRefreshing)
                {
                    s_refreshPending = true;
                    return;
                }
                s_isRefreshing = true;
            }

            try
            {
                var result = await FxvRunner.GetStatusAsync(skipScan);
                if (result.Success && result.Data != null)
                {
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            UpdateCache(result.Data);
                        }
                        finally
                        {
                            FinishRefresh(skipScan);
                        }
                    };
                    return;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FlexVault] Error updating state cache: {ex.Message}");
            }

            FinishRefresh(skipScan);
        }

        private static void FinishRefresh(bool skipScan)
        {
            bool triggerPending = false;
            lock (s_lock)
            {
                s_isRefreshing = false;
                s_lastRefreshTime = EditorApplication.timeSinceStartup;
                if (s_refreshPending)
                {
                    s_refreshPending = false;
                    triggerPending = true;
                }
            }

            if (triggerPending)
            {
                EditorApplication.delayCall += () => RefreshAsync(skipScan);
            }
        }

        private static void UpdateCache(StatusPayload status)
        {
            var newGuidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var newPathMap = new Dictionary<string, FileStatusItem>(StringComparer.OrdinalIgnoreCase);

            if (status.Files != null)
            {
                foreach (var file in status.Files)
                {
                    if (string.IsNullOrEmpty(file.Path))
                    {
                        continue;
                    }

                    string normalized = FlexVaultMetaHelper.NormalizeSeparators(file.Path);
                    newPathMap[normalized] = file;

                    string projectRelative = FlexVaultMetaHelper.ToProjectRelativePath(normalized);
                    if (!string.IsNullOrEmpty(projectRelative))
                    {
                        bool isTrackedPrefix = projectRelative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                            || projectRelative.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                            || projectRelative.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);

                        if (isTrackedPrefix)
                        {
                            string logicalAsset = FlexVaultMetaHelper.GetLogicalAssetPath(projectRelative);
                            string guid = AssetDatabase.AssetPathToGUID(logicalAsset);
                            if (!string.IsNullOrEmpty(guid))
                            {
                                string newState = file.EffectiveWorkspaceState;
                                if (newGuidMap.TryGetValue(guid, out string existingState))
                                {
                                    if (existingState.Equals("conflicted", StringComparison.OrdinalIgnoreCase) ||
                                        (newState.Equals("unchanged", StringComparison.OrdinalIgnoreCase) && !existingState.Equals("unchanged", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        continue;
                                    }
                                }
                                newGuidMap[guid] = newState;
                            }
                        }
                    }
                }
            }

            lock (s_lock)
            {
                s_latestStatus = status;
                s_pathToStatus.Clear();
                foreach (var kvp in newPathMap)
                {
                    s_pathToStatus[kvp.Key] = kvp.Value;
                }

                s_guidToState.Clear();
                foreach (var kvp in newGuidMap)
                {
                    s_guidToState[kvp.Key] = kvp.Value;
                }
            }

            OnStateChanged?.Invoke();
            EditorApplication.RepaintProjectWindow();
        }
    }
}
