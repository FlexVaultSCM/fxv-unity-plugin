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
        private static readonly List<FileStatusItem> s_changedFiles = new List<FileStatusItem>();
        private static readonly List<FileStatusItem> s_workspaceChanges = new List<FileStatusItem>();
        private static readonly List<FileStatusItem> s_unpublishedChanges = new List<FileStatusItem>();
        private static StatusPayload s_latestStatus;
        private static bool s_isRefreshing;
        private static double s_lastRefreshTime;

        public static event Action OnStateChanged;

        static FlexVaultStateCache()
        {
            EditorApplication.delayCall += async () =>
            {
                if (FlexVaultSettings.IsFlexVaultActive())
                {
                    await FxvRunner.EnsureVersionCheckedAsync();
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
                return new List<FileStatusItem>(s_changedFiles);
            }
        }

        public static List<FileStatusItem> GetWorkspaceChanges()
        {
            lock (s_lock)
            {
                return new List<FileStatusItem>(s_workspaceChanges);
            }
        }

        public static List<FileStatusItem> GetUnpublishedChanges()
        {
            lock (s_lock)
            {
                return new List<FileStatusItem>(s_unpublishedChanges);
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

            string companionPath = FlexVaultMetaHelper.IsMetaFile(path)
                ? FlexVaultMetaHelper.GetLogicalAssetPath(path)
                : FlexVaultMetaHelper.GetCompanionMetaPath(path);

            if (!string.IsNullOrEmpty(companionPath) && !string.Equals(companionPath, path, StringComparison.OrdinalIgnoreCase))
            {
                var metaItem = GetStatusByPath(companionPath);
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
                foreach (var kvp in s_pathToStatus)
                {
                    if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var f = kvp.Value;
                        if (f != null && (f.NeedsSnapshot || f.IsConflicted))
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

            string companionPath = FlexVaultMetaHelper.IsMetaFile(path)
                ? FlexVaultMetaHelper.GetLogicalAssetPath(path)
                : FlexVaultMetaHelper.GetCompanionMetaPath(path);

            if (!string.IsNullOrEmpty(companionPath) && !string.Equals(companionPath, path, StringComparison.OrdinalIgnoreCase))
            {
                var metaItem = GetStatusByPath(companionPath);
                if (metaItem != null && metaItem.IsConflicted) return true;
            }

            return false;
        }

        public static bool IsCurrentWorkspaceRevision(CommitRefJson entry)
        {
            if (entry == null) return false;

            StatusPayload status;
            lock (s_lock)
            {
                status = s_latestStatus;
            }

            if (status == null) return false;

            // 1. Match by commit hash if available
            string currentHash = status.HeadCommit?.LocalSnapshot?.CommitHash
                ?? status.HeadCommit?.PublishedHead?.CommitHash;
            if (!string.IsNullOrEmpty(currentHash) && !string.IsNullOrEmpty(entry.CommitHash))
            {
                if (string.Equals(entry.CommitHash, currentHash, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 2. Match by revision spec display (e.g. main.-.25, main.12.3, main.12)
            string currentRev = status.HeadCommit?.LocalSnapshot?.RevisionDisplay
                ?? status.HeadCommit?.PublishedHead?.RevisionDisplay
                ?? (status.SyncStatus?.SyncedRevision != null && status.CurrentBranch != null
                    ? $"{status.CurrentBranch}.{status.SyncStatus.SyncedRevision.Value}"
                    : null);

            if (!string.IsNullOrEmpty(currentRev) && !string.IsNullOrEmpty(entry.RevisionDisplay))
            {
                if (string.Equals(entry.RevisionDisplay, currentRev, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 3. Fallback to commit metadata comparison
            var currentCommit = status.HeadCommit?.LocalSnapshot?.Commit
                ?? status.HeadCommit?.PublishedHead?.Commit;

            if (currentCommit != null && entry.Commit != null)
            {
                if (string.Equals(currentCommit.Branch, entry.Commit.Branch, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(currentCommit.Type, entry.Commit.Type, StringComparison.OrdinalIgnoreCase))
                {
                    if (currentCommit.DraftRevision.HasValue && entry.Commit.DraftRevision.HasValue)
                    {
                        return currentCommit.DraftRevision.Value == entry.Commit.DraftRevision.Value;
                    }
                    if (currentCommit.Revision.HasValue && entry.Commit.Revision.HasValue)
                    {
                        return currentCommit.Revision.Value == entry.Commit.Revision.Value;
                    }
                }
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
                foreach (var kvp in s_pathToStatus)
                {
                    if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var f = kvp.Value;
                        if (f != null && f.IsConflicted)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private const double RefreshCooldownSeconds = 2.0;
        private static bool s_refreshPending;

        public static async void RefreshAsync(bool skipScan = false, bool force = false)
        {
            lock (s_lock)
            {
                if (s_isRefreshing)
                {
                    s_refreshPending = true;
                    return;
                }

                double elapsed = EditorApplication.timeSinceStartup - s_lastRefreshTime;
                if (!force && s_lastRefreshTime > 0 && elapsed < RefreshCooldownSeconds)
                {
                    if (!s_refreshPending)
                    {
                        s_refreshPending = true;
                        int delayMs = (int)Math.Max(100, (RefreshCooldownSeconds - elapsed) * 1000);
                        EditorApplication.delayCall += async () =>
                        {
                            await Task.Delay(delayMs);
                            RefreshAsync(skipScan, force: false);
                        };
                    }
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

            var newChangedFiles = new List<FileStatusItem>();
            var newWorkspaceChanges = new List<FileStatusItem>();
            var newUnpublishedChanges = new List<FileStatusItem>();

            if (status.Files != null)
            {
                foreach (var file in status.Files)
                {
                    newChangedFiles.Add(file);
                    if (file.NeedsSnapshot)
                    {
                        newWorkspaceChanges.Add(file);
                    }
                    if (file.IsUnpublished)
                    {
                        newUnpublishedChanges.Add(file);
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

                s_changedFiles.Clear();
                s_changedFiles.AddRange(newChangedFiles);

                s_workspaceChanges.Clear();
                s_workspaceChanges.AddRange(newWorkspaceChanges);

                s_unpublishedChanges.Clear();
                s_unpublishedChanges.AddRange(newUnpublishedChanges);
            }

            OnStateChanged?.Invoke();
            EditorApplication.RepaintProjectWindow();
        }
    }
}
