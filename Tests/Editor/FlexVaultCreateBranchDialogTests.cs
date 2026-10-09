using NUnit.Framework;
using FlexVault.VCS.Editor.UI;

namespace FlexVault.VCS.Editor.Tests
{
    [TestFixture]
    public class FlexVaultCreateBranchDialogTests
    {
        [Test]
        [TestCase("feature-login")]
        [TestCase("feature_login")]
        [TestCase("my branch")]
        [TestCase("test123")]
        [TestCase("A")]
        [TestCase("feature-with-many-words_and-numbers-123")]
        public void IsValidBranchName_ValidNames_ReturnsTrue(string branchName)
        {
            bool valid = FlexVaultCreateBranchDialog.IsValidBranchName(branchName, out string error);
            Assert.IsTrue(valid);
            Assert.IsNull(error);
        }

        [Test]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\t")]
        [TestCase(null)]
        public void IsValidBranchName_EmptyOrNull_ReturnsFalse(string branchName)
        {
            bool valid = FlexVaultCreateBranchDialog.IsValidBranchName(branchName, out string error);
            Assert.IsFalse(valid);
            Assert.IsNotNull(error);
            StringAssert.Contains("empty", error.ToLowerInvariant());
        }

        [Test]
        [TestCase(" leading")]
        [TestCase("trailing ")]
        [TestCase(" both ")]
        public void IsValidBranchName_EdgeWhitespace_ReturnsFalse(string branchName)
        {
            bool valid = FlexVaultCreateBranchDialog.IsValidBranchName(branchName, out string error);
            Assert.IsFalse(valid);
            Assert.IsNotNull(error);
            StringAssert.Contains("whitespace", error.ToLowerInvariant());
        }

        [Test]
        [TestCase("alice/feature")]
        [TestCase("user/my-branch")]
        public void IsValidBranchName_ContainingSlash_ReturnsFalse(string branchName)
        {
            bool valid = FlexVaultCreateBranchDialog.IsValidBranchName(branchName, out string error);
            Assert.IsFalse(valid);
            Assert.IsNotNull(error);
            StringAssert.Contains("slash", error.ToLowerInvariant());
        }

        [Test]
        [TestCase("branch.name")]
        [TestCase("feature@login")]
        [TestCase("branch#1")]
        [TestCase("feat:test")]
        public void IsValidBranchName_InvalidCharacters_ReturnsFalse(string branchName)
        {
            bool valid = FlexVaultCreateBranchDialog.IsValidBranchName(branchName, out string error);
            Assert.IsFalse(valid);
            Assert.IsNotNull(error);
        }

        [Test]
        public void IsValidBranchName_Exceeds64Characters_ReturnsFalse()
        {
            string tooLongName = new string('a', 65);
            bool valid = FlexVaultCreateBranchDialog.IsValidBranchName(tooLongName, out string error);
            Assert.IsFalse(valid);
            Assert.IsNotNull(error);
            StringAssert.Contains("64", error);

            string exactly64Name = new string('a', 64);
            valid = FlexVaultCreateBranchDialog.IsValidBranchName(exactly64Name, out error);
            Assert.IsTrue(valid);
            Assert.IsNull(error);
        }
    }
}
