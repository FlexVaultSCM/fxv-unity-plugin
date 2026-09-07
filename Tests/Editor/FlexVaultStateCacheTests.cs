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
    }
}
