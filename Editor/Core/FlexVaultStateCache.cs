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

        public static bool IsRefreshing => s_isRefreshing;
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

        private static bool s_refreshPending;

        public static async void RefreshAsync(bool skipScan = false)
        {
            if (s_isRefreshing)
            {
                s_refreshPending = true;
                return;
            }

            s_isRefreshing = true;
            try
            {
                var result = await FxvRunner.GetStatusAsync(skipScan);
                if (result.Success && result.Data != null)
                {
                    EditorApplication.delayCall += () =>
                    {
                        UpdateCache(result.Data);
                    };
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FlexVault] Error updating state cache: {ex.Message}");
            }
            finally
            {
                s_isRefreshing = false;
                s_lastRefreshTime = EditorApplication.timeSinceStartup;

                if (s_refreshPending)
                {
                    s_refreshPending = false;
                    EditorApplication.delayCall += () => RefreshAsync(skipScan);
                }
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
                                string newState = file.EffectiveState;
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
