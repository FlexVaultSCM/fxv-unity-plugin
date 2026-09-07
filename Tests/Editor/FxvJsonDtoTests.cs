using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using FlexVault.VCS.Editor.Core;

namespace FlexVault.VCS.Editor.Tests
{
    [TestFixture]
    public class FxvJsonDtoTests
    {
        [Test]
        public void StatusPayload_Deserialization_ParsesCorrectly()
        {
            string json = @"{
                ""program"": {
                    ""name"": ""fxv"",
                    ""version"": ""0.1.0"",
                    ""executable"": ""fxv.exe"",
                    ""arguments"": [""status"", ""--format"", ""json""],
                    ""invoked_at"": ""2026-09-06T12:00:00Z""
                },
                ""message"": {
                    ""kind"": ""status"",
                    ""version"": ""1.0"",
                    ""payload"": {
                        ""current_branch"": ""main"",
                        ""current_user"": ""alice"",
                        ""head_commit"": {
                            ""state"": ""clean"",
                            ""branch"": ""main"",
                            ""local_snapshot"": {
                                ""commit"": {
                                    ""branch"": ""main"",
                                    ""revision"": 5,
                                    ""type"": ""published""
                                },
                                ""commit_hash"": ""abc12345"",
                                ""description"": ""Initial commit"",
                                ""timestamp_millis_since_epoch_utc"": 1774328905000,
                                ""author_id"": ""alice"",
                                ""author_display_name"": ""Alice Smith""
                            }
                        },
                        ""sync_status"": {
                            ""up_to_date"": false,
                            ""revisions_behind"": 2,
                            ""published_head_revision"": 7,
                            ""synced_revision"": 5
                        },
                        ""files"": [
                            {
                                ""path"": ""Assets/Scenes/Main.unity"",
                                ""workspace_state"": ""modified"",
                                ""size"": 1048576
                            },
                            {
                                ""path"": ""Assets/Scripts/NewScript.cs"",
                                ""workspace_state"": ""added"",
                                ""size"": 512
                            },
                            {
                                ""path"": ""Assets/OldAsset.prefab"",
                                ""workspace_state"": ""deleted""
                            },
                            {
                                ""path"": ""Assets/Conflict.asset"",
                                ""workspace_state"": ""conflicted"",
                                ""size"": 2048
                            }
                        ],
                        ""file_change_counts"": {
                            ""total"": 4,
                            ""unpublished"": 0,
                            ""workspace_need_snapshot"": 4
                        }
                    }
                }
            }";

            var envelope = JsonConvert.DeserializeObject<OutputEnvelope<StatusPayload>>(json);

            Assert.IsNotNull(envelope);
            Assert.IsNotNull(envelope.Program);
            Assert.AreEqual("fxv", envelope.Program.Name);
            Assert.AreEqual("0.1.0", envelope.Program.Version);

            Assert.IsNotNull(envelope.Message);
            Assert.AreEqual("status", envelope.Message.Kind);

            var payload = envelope.Message.Payload;
            Assert.IsNotNull(payload);
            Assert.AreEqual("main", payload.CurrentBranch);
            Assert.AreEqual("alice", payload.CurrentUser);

            // Head commit
            Assert.IsNotNull(payload.HeadCommit);
            Assert.AreEqual("clean", payload.HeadCommit.State);
            Assert.IsNotNull(payload.HeadCommit.LocalSnapshot);
            Assert.AreEqual("Initial commit", payload.HeadCommit.LocalSnapshot.Description);
            Assert.AreEqual("Alice Smith", payload.HeadCommit.LocalSnapshot.AuthorDisplayName);
            Assert.AreEqual("main.5", payload.HeadCommit.LocalSnapshot.RevisionDisplay);

            // Sync status
            Assert.IsNotNull(payload.SyncStatus);
            Assert.IsFalse(payload.SyncStatus.UpToDate);
            Assert.AreEqual(2UL, payload.SyncStatus.RevisionsBehind);
            Assert.AreEqual(7UL, payload.SyncStatus.PublishedHeadRevision);
            Assert.AreEqual(5UL, payload.SyncStatus.SyncedRevision);

            // Files
            Assert.IsNotNull(payload.Files);
            Assert.AreEqual(4, payload.Files.Count);

            Assert.AreEqual("Assets/Scenes/Main.unity", payload.Files[0].Path);
            Assert.AreEqual("modified", payload.Files[0].EffectiveState);
            Assert.AreEqual(1048576UL, payload.Files[0].Size);

            Assert.AreEqual("Assets/Scripts/NewScript.cs", payload.Files[1].Path);
            Assert.AreEqual("added", payload.Files[1].EffectiveState);

            Assert.AreEqual("Assets/OldAsset.prefab", payload.Files[2].Path);
            Assert.AreEqual("deleted", payload.Files[2].EffectiveState);

            Assert.AreEqual("Assets/Conflict.asset", payload.Files[3].Path);
            Assert.AreEqual("conflicted", payload.Files[3].EffectiveState);
        }

        [Test]
        public void HistoryPayload_Deserialization_ParsesEntries()
        {
            string json = @"{
                ""message"": {
                    ""kind"": ""history"",
                    ""payload"": {
                        ""entries"": [
                            {
                                ""commit"": {
                                    ""branch"": ""main"",
                                    ""revision"": 12,
                                    ""type"": ""published""
                                },
                                ""commit_hash"": ""def45678"",
                                ""description"": ""Fixed character movement jump bug"",
                                ""timestamp_millis_since_epoch_utc"": 1774328905000,
                                ""author_id"": ""jane.doe"",
                                ""author_display_name"": ""Jane Doe""
                            },
                            {
                                ""commit"": {
                                    ""branch"": ""main"",
                                    ""type"": ""draft"",
                                    ""draft_revision"": 1
                                },
                                ""commit_hash"": ""draft789"",
                                ""description"": ""WIP unparented draft"",
                                ""timestamp_millis_since_epoch_utc"": 1774329000000,
                                ""author_id"": ""jane.doe"",
                                ""author_display_name"": ""Jane Doe""
                            },
                            {
                                ""commit"": {
                                    ""branch"": ""feature"",
                                    ""revision"": 3,
                                    ""type"": ""draft"",
                                    ""draft_revision"": 2
                                },
                                ""commit_hash"": ""draft321"",
                                ""description"": ""WIP parented draft"",
                                ""timestamp_millis_since_epoch_utc"": 1774329100000,
                                ""author_id"": ""jane.doe"",
                                ""author_display_name"": ""Jane Doe""
                            }
                        ]
                    }
                }
            }";

            var envelope = JsonConvert.DeserializeObject<OutputEnvelope<HistoryPayload>>(json);
            Assert.IsNotNull(envelope?.Message?.Payload);

            var entries = envelope.Message.Payload.Entries;
            Assert.AreEqual(3, entries.Count);

            // Published entry
            Assert.AreEqual("Fixed character movement jump bug", entries[0].Description);
            Assert.AreEqual("Jane Doe", entries[0].AuthorDisplayName);
            Assert.AreEqual("main.12", entries[0].RevisionDisplay);

            // Unparented draft entry (Revision is null)
            Assert.AreEqual("main.-.1", entries[1].RevisionDisplay);

            // Parented draft entry (Revision = 3, DraftRevision = 2)
            Assert.AreEqual("feature.3.2", entries[2].RevisionDisplay);
        }

        [Test]
        public void WorkspaceSyncPayload_Deserialization_ParsesConflictedAndUpdatedFiles()
        {
            string json = @"{
                ""message"": {
                    ""kind"": ""sync"",
                    ""payload"": {
                        ""target_revision"": ""main.10"",
                        ""files_updated_count"": 2,
                        ""error_count"": 1,
                        ""files_updated"": [
                            { ""path"": ""Assets/Texture.png"", ""workspace_state"": ""unchanged"" },
                            { ""path"": ""Assets/Model.fbx"", ""workspace_state"": ""unchanged"" }
                        ],
                        ""conflicted_files"": [
                            ""Assets/Conflict.prefab""
                        ]
                    }
                }
            }";

            var envelope = JsonConvert.DeserializeObject<OutputEnvelope<WorkspaceSyncPayload>>(json);
            Assert.IsNotNull(envelope?.Message?.Payload);

            var payload = envelope.Message.Payload;
            Assert.AreEqual("main.10", payload.TargetRevision);
            Assert.AreEqual(2, payload.FilesUpdatedCount);
            Assert.AreEqual(1, payload.ErrorCount);
            Assert.AreEqual(2, payload.FilesUpdated.Count);
            Assert.AreEqual(1, payload.ConflictedFiles.Count);
            Assert.AreEqual("Assets/Conflict.prefab", payload.ConflictedFiles[0]);
        }

        [Test]
        public void ErrorPayload_Deserialization_ParsesErrorMessageAndExitCode()
        {
            string json = @"{
                ""message"": {
                    ""kind"": ""error"",
                    ""payload"": {
                        ""message"": ""Workspace is locked by another process."",
                        ""exit_code"": 99,
                        ""backtrace"": ""stack trace here""
                    }
                }
            }";

            var envelope = JsonConvert.DeserializeObject<OutputEnvelope<ErrorPayload>>(json);
            Assert.IsNotNull(envelope?.Message?.Payload);
            Assert.AreEqual("error", envelope.Message.Kind);
            Assert.AreEqual("Workspace is locked by another process.", envelope.Message.Payload.Message);
            Assert.AreEqual(99, envelope.Message.Payload.ExitCode);
        }

        [Test]
        public void FileStatusItem_EffectiveState_PrefersWorkspaceOverUnpublished()
        {
            var item1 = new FileStatusItem { WorkspaceState = "modified", UnpublishedState = "added" };
            Assert.AreEqual("modified", item1.EffectiveState);

            var item2 = new FileStatusItem { WorkspaceState = null, UnpublishedState = "added" };
            Assert.AreEqual("added", item2.EffectiveState);

            var item3 = new FileStatusItem { WorkspaceState = "", UnpublishedState = null };
            Assert.AreEqual("unchanged", item3.EffectiveState);
        }
    }
}
