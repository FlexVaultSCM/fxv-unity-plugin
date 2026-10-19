using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FlexVault.VCS.Editor.Core
{
    [Serializable]
    public class OutputEnvelope<T>
    {
        [JsonProperty("program")]
        public ProgramMetadata Program { get; set; }

        [JsonProperty("message")]
        public MessageEnvelope<T> Message { get; set; }
    }

    [Serializable]
    public class ProgramMetadata
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("executable")]
        public string Executable { get; set; }

        [JsonProperty("arguments")]
        public List<string> Arguments { get; set; }

        [JsonProperty("invoked_at")]
        public string InvokedAt { get; set; }
    }

    [Serializable]
    public class MessageEnvelope<T>
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("payload")]
        public T Payload { get; set; }
    }

    [Serializable]
    public class ErrorPayload
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("exit_code")]
        public int ExitCode { get; set; }

        [JsonProperty("backtrace")]
        public string Backtrace { get; set; }
    }

    [Serializable]
    public class StatusPayload
    {
        [JsonProperty("current_branch")]
        public string CurrentBranch { get; set; }

        [JsonProperty("current_user")]
        public string CurrentUser { get; set; }

        [JsonProperty("head_commit")]
        public HeadCommitJson HeadCommit { get; set; }

        [JsonProperty("sync_status")]
        public SyncStatusJson SyncStatus { get; set; }

        [JsonProperty("files")]
        public List<FileStatusItem> Files { get; set; } = new List<FileStatusItem>();

        [JsonProperty("file_change_counts")]
        public FileChangeCountsJson FileChangeCounts { get; set; }
    }

    [Serializable]
    public class HeadCommitJson
    {
        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("local_snapshot")]
        public CommitRefJson LocalSnapshot { get; set; }

        [JsonProperty("published_head")]
        public CommitRefJson PublishedHead { get; set; }
    }

    [Serializable]
    public class SyncStatusJson
    {
        [JsonProperty("up_to_date")]
        public bool UpToDate { get; set; }

        [JsonProperty("revisions_behind")]
        public ulong RevisionsBehind { get; set; }

        [JsonProperty("published_head_revision")]
        public ulong PublishedHeadRevision { get; set; }

        [JsonProperty("synced_revision")]
        public ulong? SyncedRevision { get; set; }
    }

    [Serializable]
    public class FileChangeCountsJson
    {
        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("unpublished")]
        public int Unpublished { get; set; }

        [JsonProperty("workspace_need_snapshot")]
        public int WorkspaceNeedSnapshot { get; set; }
    }

    /// Why a file is in conflict.
    [Serializable]
    public class ConflictState
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonIgnore]
        public string Description
        {
            get
            {
                switch (Kind)
                {
                    case "content": return "Both sides changed this file's content.";
                    case "deleted": return "One side deleted this path while the other changed it.";
                    case "type_change": return "One side replaced this path with a directory.";
                    default: return "This path is in conflict.";
                }
            }
        }
    }

    [Serializable]
    public class FileStatusItem
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("unpublished_state")]
        public string UnpublishedState { get; set; }

        [JsonProperty("workspace_state")]
        public string WorkspaceState { get; set; }

        [JsonProperty("conflict_state")]
        public ConflictState ConflictState { get; set; }

        [JsonProperty("size")]
        public ulong? Size { get; set; }

        [JsonIgnore]
        public bool IsConflicted =>
            ConflictState != null ||
            "conflicted".Equals(WorkspaceState, StringComparison.OrdinalIgnoreCase) ||
            "conflicted".Equals(UnpublishedState, StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool NeedsSnapshot => !string.IsNullOrEmpty(WorkspaceState) &&
            !WorkspaceState.Equals("unchanged", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsUnpublished => !string.IsNullOrEmpty(UnpublishedState) &&
            !UnpublishedState.Equals("unchanged", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public string EffectiveWorkspaceState
        {
            get
            {
                if (IsConflicted)
                {
                    return "conflicted";
                }
                if (!string.IsNullOrEmpty(WorkspaceState) && !WorkspaceState.Equals("unchanged", StringComparison.OrdinalIgnoreCase))
                {
                    if (WorkspaceState.Equals("maybe_changed", StringComparison.OrdinalIgnoreCase))
                    {
                        return "modified";
                    }
                    return WorkspaceState;
                }
                return "unchanged";
            }
        }

        [JsonIgnore]
        public string EffectiveState
        {
            get
            {
                if (IsConflicted)
                {
                    return "conflicted";
                }
                if (!string.IsNullOrEmpty(WorkspaceState) && !WorkspaceState.Equals("unchanged", StringComparison.OrdinalIgnoreCase))
                {
                    if (WorkspaceState.Equals("maybe_changed", StringComparison.OrdinalIgnoreCase))
                    {
                        return "modified";
                    }
                    return WorkspaceState;
                }
                if (!string.IsNullOrEmpty(UnpublishedState) && !UnpublishedState.Equals("unchanged", StringComparison.OrdinalIgnoreCase))
                {
                    return UnpublishedState;
                }
                return "unchanged";
            }
        }
    }

    [Serializable]
    public class WorkspaceSyncPayload
    {
        [JsonProperty("target_revision")]
        public string TargetRevision { get; set; }

        [JsonProperty("files_updated_count")]
        public int FilesUpdatedCount { get; set; }

        [JsonProperty("error_count")]
        public int ErrorCount { get; set; }

        [JsonProperty("files_updated")]
        public List<FileStatusItem> FilesUpdated { get; set; } = new List<FileStatusItem>();

        [JsonProperty("conflicted_files")]
        public List<string> ConflictedFiles { get; set; } = new List<string>();
    }

    [Serializable]
    public class CommitInfoDetailJson
    {
        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("revision")]
        public ulong? Revision { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("draft_revision")]
        public ulong? DraftRevision { get; set; }
    }

    [Serializable]
    public class CommitRefJson
    {
        [JsonProperty("commit")]
        public CommitInfoDetailJson Commit { get; set; }

        [JsonProperty("commit_hash")]
        public string CommitHash { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("timestamp_millis_since_epoch_utc")]
        public long TimestampMillisSinceEpochUtc { get; set; }

        [JsonProperty("author_id")]
        public string AuthorId { get; set; }

        [JsonProperty("author_display_name")]
        public string AuthorDisplayName { get; set; }

        [JsonIgnore]
        public DateTime TimestampUtc => DateTimeOffset.FromUnixTimeMilliseconds(TimestampMillisSinceEpochUtc).UtcDateTime;

        [JsonIgnore]
        public string RevisionDisplay
        {
            get
            {
                if (Commit == null) return "unknown";
                if (Commit.Type == "draft" && Commit.DraftRevision.HasValue && Commit.DraftRevision.Value > 0)
                {
                    return Commit.Revision.HasValue
                        ? $"{Commit.Branch}.{Commit.Revision.Value}.{Commit.DraftRevision.Value}"
                        : $"{Commit.Branch}.unpublished.{Commit.DraftRevision.Value}";
                }
                return Commit.Revision.HasValue ? $"{Commit.Branch}.{Commit.Revision.Value}" : Commit.Branch;
            }
        }

        // Same shape as RevisionDisplay, but formatted for feeding back into the fxv CLI
        // (changeinfo, cat -r, goto, branch new --from, etc.). The CLI's revision-spec parser
        // expects a literal "-" placeholder when no published revision exists yet (e.g. "main.-.1"),
        // whereas RevisionDisplay uses "unpublished" for human readability.
        [JsonIgnore]
        public string RevisionSpec
        {
            get
            {
                if (Commit == null) return string.Empty;
                if (Commit.Type == "draft" && Commit.DraftRevision.HasValue && Commit.DraftRevision.Value > 0)
                {
                    return Commit.Revision.HasValue
                        ? $"{Commit.Branch}.{Commit.Revision.Value}.{Commit.DraftRevision.Value}"
                        : $"{Commit.Branch}.-.{Commit.DraftRevision.Value}";
                }
                return Commit.Revision.HasValue ? $"{Commit.Branch}.{Commit.Revision.Value}" : (Commit.Branch ?? string.Empty);
            }
        }

        // Resolves the predecessor revision in a newest-first history list, matching the parent
        // commit that 'fxv changeinfo' diffs against:
        // - A published commit (e.g. main.3) continues the previous published commit (main.2),
        //   NOT the draft below it in the combined history list (main.2.6), whose tree is often
        //   identical to main.3 because main.3 was published from main.2.6.
        // - A draft commit at draft_revision == 1 (e.g. main.2.1) continues its published base
        //   revision (main.2).
        // - A subsequent draft commit (e.g. main.2.6) continues the preceding draft or published
        //   entry immediately following it in the history list.
        public static CommitRefJson GetPredecessorEntry(IReadOnlyList<CommitRefJson> entries, int currentIndex)
        {
            if (entries == null || currentIndex < 0 || currentIndex >= entries.Count)
            {
                return null;
            }

            var current = entries[currentIndex];
            bool isPublished = string.Equals(current?.Commit?.Type, "published", StringComparison.OrdinalIgnoreCase);

            if (isPublished)
            {
                for (int i = currentIndex + 1; i < entries.Count; i++)
                {
                    var candidate = entries[i];
                    if (string.Equals(candidate?.Commit?.Type, "published", StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
                return null;
            }

            if (current?.Commit?.DraftRevision == 1 && current.Commit.Revision.HasValue)
            {
                ulong baseRev = current.Commit.Revision.Value;
                for (int i = currentIndex + 1; i < entries.Count; i++)
                {
                    var candidate = entries[i];
                    if (string.Equals(candidate?.Commit?.Type, "published", StringComparison.OrdinalIgnoreCase) &&
                        candidate.Commit.Revision == baseRev)
                    {
                        return candidate;
                    }
                }
            }

            return currentIndex + 1 < entries.Count ? entries[currentIndex + 1] : null;
        }
    }

    [Serializable]
    public class HistoryPayload
    {
        [JsonProperty("entries")]
        public List<CommitRefJson> Entries { get; set; } = new List<CommitRefJson>();
    }

    [Serializable]
    public class ChangeInfoSummary
    {
        [JsonProperty("total_changed")]
        public int TotalChanged { get; set; }

        [JsonProperty("added")]
        public int Added { get; set; }

        [JsonProperty("modified")]
        public int Modified { get; set; }

        [JsonProperty("deleted")]
        public int Deleted { get; set; }
    }

    [Serializable]
    public class ChangeInfoItem
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("size")]
        public long? Size { get; set; }

        [JsonProperty("old_hash")]
        public string OldHash { get; set; }

        [JsonProperty("new_hash")]
        public string NewHash { get; set; }
    }

    [Serializable]
    public class ChangeInfoPayload
    {
        [JsonProperty("commit")]
        public CommitInfoDetailJson Commit { get; set; }

        [JsonProperty("commit_hash")]
        public string CommitHash { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("timestamp_millis_since_epoch_utc")]
        public long TimestampMillisSinceEpochUtc { get; set; }

        [JsonProperty("author_id")]
        public string AuthorId { get; set; }

        [JsonProperty("author_display_name")]
        public string AuthorDisplayName { get; set; }

        [JsonProperty("summary")]
        public ChangeInfoSummary Summary { get; set; }

        [JsonProperty("changes")]
        public List<ChangeInfoItem> Changes { get; set; } = new List<ChangeInfoItem>();
    }

    [Serializable]
    public class BranchInfo
    {
        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("branch_unique_id")]
        public string BranchUniqueId { get; set; }

        [JsonProperty("branch_type")]
        public string BranchType { get; set; }

        [JsonProperty("owner")]
        public string Owner { get; set; }

        [JsonProperty("published_head")]
        public string PublishedHead { get; set; }

        [JsonProperty("draft_head")]
        public string DraftHead { get; set; }

        [JsonProperty("local_only")]
        public bool LocalOnly { get; set; }

        [JsonProperty("retired")]
        public bool Retired { get; set; }
    }

    [Serializable]
    public class BranchListPayload
    {
        [JsonProperty("branches")]
        public List<BranchInfo> Branches { get; set; } = new List<BranchInfo>();
    }

    [Serializable]
    public class BranchNewPayload
    {
        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("branch_type")]
        public string BranchType { get; set; }

        [JsonProperty("revision")]
        public string Revision { get; set; }

        [JsonProperty("source_revision")]
        public string SourceRevision { get; set; }

        [JsonProperty("switched")]
        public bool Switched { get; set; }
    }

    [Serializable]
    public class FxvFetchPayload
    {
        [JsonProperty("bytes_downloaded")]
        public long BytesDownloaded { get; set; }

        [JsonProperty("repaired_files")]
        public List<string> RepairedFiles { get; set; } = new List<string>();
    }
}
