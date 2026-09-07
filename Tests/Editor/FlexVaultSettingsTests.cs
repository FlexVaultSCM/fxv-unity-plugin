using System;
using System.IO;
using NUnit.Framework;
using FlexVault.VCS.Editor.Core;

namespace FlexVault.VCS.Editor.Tests
{
    [TestFixture]
    public class FlexVaultSettingsTests
    {
        private string m_originalCustomPath;

        [SetUp]
        public void SetUp()
        {
            m_originalCustomPath = FlexVaultSettings.CustomBinaryPath;
            FlexVaultSettings.InvalidateRepoRoot();
        }

        [TearDown]
        public void TearDown()
        {
            FlexVaultSettings.CustomBinaryPath = m_originalCustomPath;
            FlexVaultSettings.InvalidateRepoRoot();
        }

        [Test]
        public void CustomBinaryPath_RoundTrips()
        {
            const string testPath = @"C:\FakePath\fxv.exe";
            FlexVaultSettings.CustomBinaryPath = testPath;
            Assert.AreEqual(testPath, FlexVaultSettings.CustomBinaryPath);
        }

        [Test]
        public void GetEffectiveBinaryPath_WhenCustomFileExists_UsesCustom()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "fxv_test_binary_" + Guid.NewGuid().ToString("N") + ".exe");
            File.WriteAllText(tempFile, "stub");
            try
            {
                FlexVaultSettings.CustomBinaryPath = tempFile;
                string effective = FlexVaultSettings.GetEffectiveBinaryPath();
                Assert.AreEqual(tempFile, effective);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void GetEffectiveBinaryPath_WhenCustomFileDoesNotExist_FallsBack()
        {
            FlexVaultSettings.CustomBinaryPath = @"C:\NonexistentDirectory\fxv_nonexistent.exe";
            string effective = FlexVaultSettings.GetEffectiveBinaryPath();
            Assert.AreNotEqual(@"C:\NonexistentDirectory\fxv_nonexistent.exe", effective);
            Assert.IsNotEmpty(effective);
        }

        [Test]
        public void GetRepositoryRoot_ReturnsNonEmptyPath()
        {
            string root = FlexVaultSettings.GetRepositoryRoot();
            Assert.IsNotNull(root);
            Assert.IsNotEmpty(root);
            Assert.IsFalse(root.Contains("\\"), "Repo root must have normalized forward slashes.");
        }
    }
}
