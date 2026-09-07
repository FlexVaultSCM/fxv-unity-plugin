using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FlexVault.VCS.Editor.Core;

namespace FlexVault.VCS.Editor.Tests
{
    [TestFixture]
    public class FlexVaultMetaHelperTests
    {
        [Test]
        public void NormalizeSeparators_BackslashesAndTrailingSlashes_NormalizedCorrectly()
        {
            Assert.AreEqual("Assets/Scenes/Main.unity", FlexVaultMetaHelper.NormalizeSeparators(@"Assets\Scenes\Main.unity"));
            Assert.AreEqual("Assets/Scenes", FlexVaultMetaHelper.NormalizeSeparators(@"Assets/Scenes/"));
            Assert.AreEqual("Assets/Scenes", FlexVaultMetaHelper.NormalizeSeparators(@"Assets\Scenes\"));
            Assert.AreEqual(string.Empty, FlexVaultMetaHelper.NormalizeSeparators(null));
            Assert.AreEqual(string.Empty, FlexVaultMetaHelper.NormalizeSeparators(string.Empty));
        }

        [Test]
        public void IsMetaFile_RecognizesMetaExtension()
        {
            Assert.IsTrue(FlexVaultMetaHelper.IsMetaFile("Assets/Scripts/Player.cs.meta"));
            Assert.IsTrue(FlexVaultMetaHelper.IsMetaFile("Assets/Scenes/Main.unity.META"));
            Assert.IsFalse(FlexVaultMetaHelper.IsMetaFile("Assets/Scripts/Player.cs"));
            Assert.IsFalse(FlexVaultMetaHelper.IsMetaFile("Assets/Scripts/Player.cs.meta.bak"));
            Assert.IsFalse(FlexVaultMetaHelper.IsMetaFile(null));
            Assert.IsFalse(FlexVaultMetaHelper.IsMetaFile(string.Empty));
        }

        [Test]
        public void GetCompanionMetaPath_ReturnsCorrectMetaPath()
        {
            Assert.AreEqual("Assets/Scripts/Player.cs.meta", FlexVaultMetaHelper.GetCompanionMetaPath("Assets/Scripts/Player.cs"));
            Assert.AreEqual("Assets/Scripts.meta", FlexVaultMetaHelper.GetCompanionMetaPath("Assets/Scripts"));
            Assert.AreEqual("Assets/Scripts.meta", FlexVaultMetaHelper.GetCompanionMetaPath("Assets/Scripts.meta"));
            Assert.AreEqual(string.Empty, FlexVaultMetaHelper.GetCompanionMetaPath(null));
        }

        [Test]
        public void GetLogicalAssetPath_StripsMetaExtension()
        {
            Assert.AreEqual("Assets/Scripts/Player.cs", FlexVaultMetaHelper.GetLogicalAssetPath("Assets/Scripts/Player.cs.meta"));
            Assert.AreEqual("Assets/Scripts", FlexVaultMetaHelper.GetLogicalAssetPath("Assets/Scripts.meta"));
            Assert.AreEqual("Assets/Scripts/Player.cs", FlexVaultMetaHelper.GetLogicalAssetPath("Assets/Scripts/Player.cs"));
            Assert.AreEqual(string.Empty, FlexVaultMetaHelper.GetLogicalAssetPath(null));
        }

        [Test]
        public void ExpandWithMeta_SingleFile_IncludesBothAssetAndMeta()
        {
            var input = new List<string> { "Assets/Scripts/Player.cs" };
            var expanded = FlexVaultMetaHelper.ExpandWithMeta(input);

            CollectionAssert.Contains(expanded, FlexVaultMetaHelper.ToRepoRelativePath(FlexVaultMetaHelper.ToAbsolutePath("Assets/Scripts/Player.cs")));
            CollectionAssert.Contains(expanded, FlexVaultMetaHelper.ToRepoRelativePath(FlexVaultMetaHelper.ToAbsolutePath("Assets/Scripts/Player.cs.meta")));
        }

        [Test]
        public void ExpandWithMeta_MetaFile_IncludesCompanionAsset()
        {
            var input = new List<string> { "Assets/Scripts/Player.cs.meta" };
            var expanded = FlexVaultMetaHelper.ExpandWithMeta(input);

            CollectionAssert.Contains(expanded, FlexVaultMetaHelper.ToRepoRelativePath(FlexVaultMetaHelper.ToAbsolutePath("Assets/Scripts/Player.cs")));
            CollectionAssert.Contains(expanded, FlexVaultMetaHelper.ToRepoRelativePath(FlexVaultMetaHelper.ToAbsolutePath("Assets/Scripts/Player.cs.meta")));
        }

        [Test]
        public void ExpandWithMeta_NullOrEmptyInput_ReturnsEmptyList()
        {
            var nullResult = FlexVaultMetaHelper.ExpandWithMeta(null);
            Assert.IsNotNull(nullResult);
            Assert.AreEqual(0, nullResult.Count);

            var emptyResult = FlexVaultMetaHelper.ExpandWithMeta(new List<string>());
            Assert.IsNotNull(emptyResult);
            Assert.AreEqual(0, emptyResult.Count);
        }

        [Test]
        public void ExpandWithMeta_ExistingDirectory_ExpandsChildrenAndMetaRecursively()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FxvTestDir_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string subDir = Path.Combine(tempDir, "SubDir");
                Directory.CreateDirectory(subDir);
                File.WriteAllText(Path.Combine(tempDir, "File1.txt"), "Test1");
                File.WriteAllText(Path.Combine(tempDir, "File1.txt.meta"), "Meta1");
                File.WriteAllText(Path.Combine(subDir, "File2.txt"), "Test2");

                var input = new List<string> { tempDir };
                var expanded = FlexVaultMetaHelper.ExpandWithMeta(input);

                string repoRelSubDir = FlexVaultMetaHelper.ToRepoRelativePath(subDir);
                string repoRelFile1 = FlexVaultMetaHelper.ToRepoRelativePath(Path.Combine(tempDir, "File1.txt"));
                string repoRelFile2 = FlexVaultMetaHelper.ToRepoRelativePath(Path.Combine(subDir, "File2.txt"));

                CollectionAssert.Contains(expanded, repoRelSubDir);
                CollectionAssert.Contains(expanded, repoRelSubDir + ".meta");
                CollectionAssert.Contains(expanded, repoRelFile1);
                CollectionAssert.Contains(expanded, repoRelFile1 + ".meta");
                CollectionAssert.Contains(expanded, repoRelFile2);
                CollectionAssert.Contains(expanded, repoRelFile2 + ".meta");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
