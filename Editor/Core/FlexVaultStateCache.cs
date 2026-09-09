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

                    string cliVersion = FlexVaultVersionGuard.LastVersionString;
                    if (FlexVaultVersionGuard.IsVersionCompatible == false)
                    {
                        Debug.LogError($"[FlexVault] Plugin loaded at version {FlexVaultVersionGuard.PluginVersion}, but CLI version check failed: {FlexVaultVersionGuard.LastErrorMessage}");
                    }
                    else if (!string.IsNullOrEmpty(cliVersion))
                    {
                        Debug.Log($"[FlexVault] Plugin loaded at version {FlexVaultVersionGuard.PluginVersion} (CLI version {cliVersion})");
                    }
                    else
                    {
                        Debug.Log($"[FlexVault] Plugin loaded at version {FlexVaultVersionGuard.PluginVersion}");
                    }

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

        public static bool IsCurrentWorkspaceRevision(CommitRefJson entry, IEnumerable<CommitRefJson> allEntries = null)
        {
            if (entry == null) return false;

            StatusPayload status;
            lock (s_lock)
            {
                status = s_latestStatus;
            }

            if (status == null) return false;

            // Ensure branch matches if both are known
            if (!string.IsNullOrEmpty(status.CurrentBranch) && !string.IsNullOrEmpty(entry.Commit?.Branch))
            {
                if (!string.Equals(entry.Commit.Branch, status.CurrentBranch, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // 1. Exact draft snapshot match:
            // If the workspace is currently on a draft (LocalSnapshot), check if this entry is that exact draft.
            var localSnapshot = status.HeadCommit?.LocalSnapshot;
            if (localSnapshot != null && IsDraftMatch(entry, localSnapshot))
            {
                return true;
            }

            // 2. Published baseline match:
            // If this entry is a published commit, check if it matches the published revision the workspace is synced to.
            bool isPublished = string.Equals(entry.Commit?.Type, "published", StringComparison.OrdinalIgnoreCase)
                || (entry.Commit != null && !entry.Commit.DraftRevision.HasValue);

            if (isPublished)
            {
                // If a list of visible history entries is provided, and one of the entries is an active draft that
                // matches LocalSnapshot, that draft is the active version and the published parent should not be
                // highlighted as the current version.
                if (allEntries != null && localSnapshot != null)
                {
                    bool hasActiveDraftInList = false;
                    foreach (var other in allEntries)
                    {
                        if (other == null || ReferenceEquals(other, entry)) continue;
                        if (IsDraftMatch(other, localSnapshot))
                        {
                            hasActiveDraftInList = true;
                            break;
                        }
                    }

                    if (hasActiveDraftInList)
                    {
                        return false;
                    }
                }

                // Determine the workspace's current published revision:
                // Primary source is SyncStatus.SyncedRevision (where the workspace is synced to).
                // Fallbacks: LocalSnapshot parent revision, or PublishedHead revision when up to date.
                ulong? currentPublishedRev = status.SyncStatus?.SyncedRevision
                    ?? localSnapshot?.Commit?.Revision
                    ?? (status.SyncStatus == null || status.SyncStatus.UpToDate ? status.HeadCommit?.PublishedHead?.Commit?.Revision : null);

                if (currentPublishedRev.HasValue)
                {
                    if (entry.Commit?.Revision.HasValue == true && entry.Commit.Revision.Value == currentPublishedRev.Value)
                    {
                        return true;
                    }

                    string expectedRevDisplay = !string.IsNullOrEmpty(status.CurrentBranch)
                        ? $"{status.CurrentBranch}.{currentPublishedRev.Value}"
                        : currentPublishedRev.Value.ToString();

                    if (!string.IsNullOrEmpty(entry.RevisionDisplay) &&
                        string.Equals(entry.RevisionDisplay, expectedRevDisplay, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // Match against PublishedHead by commit hash if available and up-to-date
                if (!string.IsNullOrEmpty(entry.CommitHash) && !string.IsNullOrEmpty(status.HeadCommit?.PublishedHead?.CommitHash))
                {
                    if (string.Equals(entry.CommitHash, status.HeadCommit.PublishedHead.CommitHash, StringComparison.OrdinalIgnoreCase))
                    {
                        if (status.SyncStatus == null || status.SyncStatus.UpToDate ||
                            (status.SyncStatus.SyncedRevision.HasValue && status.HeadCommit.PublishedHead.Commit?.Revision == status.SyncStatus.SyncedRevision))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsDraftMatch(CommitRefJson entry, CommitRefJson localSnapshot)
        {
            if (entry == null || localSnapshot == null) return false;

            // Commit hash match if both have it
            if (!string.IsNullOrEmpty(entry.CommitHash) && !string.IsNullOrEmpty(localSnapshot.CommitHash))
            {
                if (string.Equals(entry.CommitHash, localSnapshot.CommitHash, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // RevisionDisplay spec match (e.g. main.4.2 or main.-.1)
            if (!string.IsNullOrEmpty(entry.RevisionDisplay) && !string.IsNullOrEmpty(localSnapshot.RevisionDisplay))
            {
                if (string.Equals(entry.RevisionDisplay, localSnapshot.RevisionDisplay, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Commit metadata match
            if (entry.Commit != null && localSnapshot.Commit != null &&
                string.Equals(entry.Commit.Type, "draft", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(localSnapshot.Commit.Type, "draft", StringComparison.OrdinalIgnoreCase))
            {
                if (entry.Commit.DraftRevision.HasValue && localSnapshot.Commit.DraftRevision.HasValue &&
                    entry.Commit.DraftRevision.Value == localSnapshot.Commit.DraftRevision.Value)
                {
                    return entry.Commit.Revision == localSnapshot.Commit.Revision;
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
                else if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    UnityEngine.Debug.LogWarning($"[FlexVault] State cache refresh failed: {result.ErrorMessage}");
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
            if (status == null)
            {
                lock (s_lock)
                {
                    s_latestStatus = null;
                    s_pathToStatus.Clear();
                    s_guidToState.Clear();
                    s_changedFiles.Clear();
                    s_workspaceChanges.Clear();
                    s_unpublishedChanges.Clear();
                }
                OnStateChanged?.Invoke();
                try { EditorApplication.RepaintProjectWindow(); } catch { }
                return;
            }

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
                            string guid = null;
                            try { guid = AssetDatabase.AssetPathToGUID(logicalAsset); } catch { }
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
            try { EditorApplication.RepaintProjectWindow(); } catch { }
        }
    }
}
