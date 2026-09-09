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
        public object ConflictState { get; set; }

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
                if (Commit.Type == "draft" && Commit.DraftRevision.HasValue)
                {
                    return Commit.Revision.HasValue
                        ? $"{Commit.Branch}.{Commit.Revision.Value}.{Commit.DraftRevision.Value}"
                        : $"{Commit.Branch}.-.{Commit.DraftRevision.Value}";
                }
                return Commit.Revision.HasValue ? $"{Commit.Branch}.{Commit.Revision.Value}" : Commit.Branch;
            }
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
}
