using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using FlexVault.VCS.Editor.Core;

namespace FlexVault.VCS.Editor.Tests
{
    [TestFixture]
    public class FlexVaultStateCacheTests
    {
        private static void InvokeUpdateCache(StatusPayload payload)
        {
            var method = typeof(FlexVaultStateCache).GetMethod("UpdateCache", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "UpdateCache method should exist on FlexVaultStateCache");
            method.Invoke(null, new object[] { payload });
        }

        [Test]
        public void StateCache_UpdateCache_PopulatesPathAndLatestStatus()
        {
            var payload = new StatusPayload
            {
                CurrentBranch = "main",
                CurrentUser = "alice",
                Files = new List<FileStatusItem>
                {
                    new FileStatusItem { Path = "Assets/Scenes/Main.unity", WorkspaceState = "modified" },
                    new FileStatusItem { Path = "Assets/Scripts/Player.cs", WorkspaceState = "added" },
                    new FileStatusItem { Path = "Assets/Old.mat", WorkspaceState = "deleted" },
                    new FileStatusItem { Path = "Assets/Conflicted.prefab", WorkspaceState = "conflicted" }
                }
            };

            InvokeUpdateCache(payload);

            Assert.IsNotNull(FlexVaultStateCache.LatestStatus);
            Assert.AreEqual("main", FlexVaultStateCache.LatestStatus.CurrentBranch);
            Assert.AreEqual("alice", FlexVaultStateCache.LatestStatus.CurrentUser);

            var changedFiles = FlexVaultStateCache.GetChangedFiles();
            Assert.AreEqual(4, changedFiles.Count);

            var item1 = FlexVaultStateCache.GetStatusByPath("Assets/Scenes/Main.unity");
            Assert.IsNotNull(item1);
            Assert.AreEqual("modified", item1.EffectiveState);

            var item2 = FlexVaultStateCache.GetStatusByPath("Assets/Scripts/Player.cs");
            Assert.IsNotNull(item2);
            Assert.AreEqual("added", item2.EffectiveState);

            var item3 = FlexVaultStateCache.GetStatusByPath("Assets/Old.mat");
            Assert.IsNotNull(item3);
            Assert.AreEqual("deleted", item3.EffectiveState);

            var item4 = FlexVaultStateCache.GetStatusByPath("Assets/Conflicted.prefab");
            Assert.IsNotNull(item4);
            Assert.AreEqual("conflicted", item4.EffectiveState);
        }

        [Test]
        public void StateCache_OnStateChanged_FiresWhenCacheUpdates()
        {
            bool eventFired = false;
            Action handler = () => { eventFired = true; };
            FlexVaultStateCache.OnStateChanged += handler;

            try
            {
                var payload = new StatusPayload
                {
                    CurrentBranch = "feature",
                    Files = new List<FileStatusItem>()
                };

                InvokeUpdateCache(payload);
                Assert.IsTrue(eventFired);
            }
            finally
            {
                FlexVaultStateCache.OnStateChanged -= handler;
            }
        }

        [Test]
        public void StateCache_EmptyOrNullQuery_ReturnsNull()
        {
            Assert.IsNull(FlexVaultStateCache.GetStatusByPath(null));
            Assert.IsNull(FlexVaultStateCache.GetStatusByPath(string.Empty));
            Assert.IsNull(FlexVaultStateCache.GetStateByGuid(null));
            Assert.IsNull(FlexVaultStateCache.GetStateByGuid(string.Empty));
        }

        [Test]
        public void StateCache_RevertTransitionsToUnchanged()
        {
            // Initial state: modified
            var initialPayload = new StatusPayload
            {
                Files = new List<FileStatusItem>
                {
                    new FileStatusItem { Path = "Assets/RevertedFile.cs", WorkspaceState = "modified" }
                }
            };
            InvokeUpdateCache(initialPayload);

            var pre = FlexVaultStateCache.GetStatusByPath("Assets/RevertedFile.cs");
            Assert.IsNotNull(pre);
            Assert.AreEqual("modified", pre.EffectiveState);

            // After revert: clean / unchanged
            var postPayload = new StatusPayload
            {
                Files = new List<FileStatusItem>()
            };
            InvokeUpdateCache(postPayload);

            var post = FlexVaultStateCache.GetStatusByPath("Assets/RevertedFile.cs");
            Assert.IsNull(post);
            Assert.AreEqual(0, FlexVaultStateCache.GetChangedFiles().Count);
        }

        [Test]
        public void StateCache_WorkspaceVsUnpublishedChanges_FiltersAccurately()
        {
            var payload = new StatusPayload
            {
                Files = new List<FileStatusItem>
                {
                    new FileStatusItem { Path = "Assets/DirtyWorkspace.cs", WorkspaceState = "modified", UnpublishedState = null },
                    new FileStatusItem { Path = "Assets/SnapshottedDraft.cs", WorkspaceState = null, UnpublishedState = "added" },
                    new FileStatusItem { Path = "Assets/Both.cs", WorkspaceState = "modified", UnpublishedState = "added" }
                }
            };
            InvokeUpdateCache(payload);

            var workspaceChanges = FlexVaultStateCache.GetWorkspaceChanges();
            var unpublishedChanges = FlexVaultStateCache.GetUnpublishedChanges();
            var allChanges = FlexVaultStateCache.GetChangedFiles();

            Assert.AreEqual(2, workspaceChanges.Count, "Should only include files with WorkspaceState");
            Assert.IsTrue(workspaceChanges.Exists(f => f.Path == "Assets/DirtyWorkspace.cs"));
            Assert.IsTrue(workspaceChanges.Exists(f => f.Path == "Assets/Both.cs"));
            Assert.IsFalse(workspaceChanges.Exists(f => f.Path == "Assets/SnapshottedDraft.cs"));

            Assert.AreEqual(2, unpublishedChanges.Count, "Should only include files with UnpublishedState");
            Assert.IsTrue(unpublishedChanges.Exists(f => f.Path == "Assets/SnapshottedDraft.cs"));
            Assert.IsTrue(unpublishedChanges.Exists(f => f.Path == "Assets/Both.cs"));
            Assert.IsFalse(unpublishedChanges.Exists(f => f.Path == "Assets/DirtyWorkspace.cs"));

            Assert.AreEqual(3, allChanges.Count);

            // Simulate snapshotting: DirtyWorkspace becomes clean in workspace, now unpublished in draft
            var afterSnapshot = new StatusPayload
            {
                Files = new List<FileStatusItem>
                {
                    new FileStatusItem { Path = "Assets/DirtyWorkspace.cs", WorkspaceState = null, UnpublishedState = "modified" },
                    new FileStatusItem { Path = "Assets/SnapshottedDraft.cs", WorkspaceState = null, UnpublishedState = "added" },
                    new FileStatusItem { Path = "Assets/Both.cs", WorkspaceState = null, UnpublishedState = "added" }
                }
            };
            InvokeUpdateCache(afterSnapshot);

            Assert.AreEqual(0, FlexVaultStateCache.GetWorkspaceChanges().Count, "All workspace changes should be cleared after snapshot");
            Assert.AreEqual(3, FlexVaultStateCache.GetUnpublishedChanges().Count, "All files should now be recorded in unpublished draft");
        }

        [Test]
        public void StateCache_PendingAndConflictQueries_ReturnCorrectStatus()
        {
            var payload = new StatusPayload
            {
                Files = new List<FileStatusItem>
                {
                    new FileStatusItem { Path = "Assets/FolderA/Modified.cs", WorkspaceState = "modified" },
                    new FileStatusItem { Path = "Assets/FolderB/Conflicted.cs", WorkspaceState = "conflicted" },
                    new FileStatusItem { Path = "Assets/FolderC/DraftOnly.cs", UnpublishedState = "added" }
                }
            };
            InvokeUpdateCache(payload);

            // Pending changes checks: only dirtied workspace files or conflicts are pending
            Assert.IsTrue(FlexVaultStateCache.HasPendingChanges("Assets/FolderA/Modified.cs"));
            Assert.IsTrue(FlexVaultStateCache.HasPendingChanges("Assets/FolderB/Conflicted.cs"));
            Assert.IsFalse(FlexVaultStateCache.HasPendingChanges("Assets/FolderC/DraftOnly.cs"), "Draft-only clean files must not be reported as pending changes");
            Assert.IsFalse(FlexVaultStateCache.HasPendingChanges("Assets/FolderA/CleanFile.cs"));
            Assert.IsFalse(FlexVaultStateCache.HasPendingChanges("Assets/NonExistent.cs"));

            // Folder checks: only folders with dirty workspace files or conflicts are pending
            Assert.IsTrue(FlexVaultStateCache.HasPendingChangesInFolder("Assets/FolderA"));
            Assert.IsTrue(FlexVaultStateCache.HasPendingChangesInFolder("Assets/FolderB"));
            Assert.IsFalse(FlexVaultStateCache.HasPendingChangesInFolder("Assets/FolderC"), "Folders with only clean draft files must not be reported as pending");
            Assert.IsFalse(FlexVaultStateCache.HasPendingChangesInFolder("Assets/CleanFolder"));

            // Conflict checks
            Assert.IsTrue(FlexVaultStateCache.IsFileConflicted("Assets/FolderB/Conflicted.cs"));
            Assert.IsFalse(FlexVaultStateCache.IsFileConflicted("Assets/FolderA/Modified.cs"));
            Assert.IsTrue(FlexVaultStateCache.HasConflictInFolder("Assets/FolderB"));
            Assert.IsFalse(FlexVaultStateCache.HasConflictInFolder("Assets/FolderA"));
        }
    }
}
