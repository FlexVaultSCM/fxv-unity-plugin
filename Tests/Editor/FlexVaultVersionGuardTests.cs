using NUnit.Framework;
using FlexVault.VCS.Editor.Core;

namespace FlexVault.VCS.Editor.Tests
{
    [TestFixture]
    public class FlexVaultVersionGuardTests
    {
        [Test]
        public void TryParse_ValidSemVerStrings_ParsesCorrectly()
        {
            Assert.IsTrue(FxvCliVersion.TryParse("0.1.0", out var v1));
            Assert.AreEqual(0, v1.Major);
            Assert.AreEqual(1, v1.Minor);
            Assert.AreEqual(0, v1.Patch);

            Assert.IsTrue(FxvCliVersion.TryParse("0.6.2", out var v2));
            Assert.AreEqual(0, v2.Major);
            Assert.AreEqual(6, v2.Minor);
            Assert.AreEqual(2, v2.Patch);

            Assert.IsTrue(FxvCliVersion.TryParse("1.0", out var v3));
            Assert.AreEqual(1, v3.Major);
            Assert.AreEqual(0, v3.Minor);
            Assert.AreEqual(0, v3.Patch);

            Assert.IsTrue(FxvCliVersion.TryParse("0.5.1-beta+build123", out var v4));
            Assert.AreEqual(0, v4.Major);
            Assert.AreEqual(5, v4.Minor);
            Assert.AreEqual(1, v4.Patch);
        }

        [Test]
        public void TryParse_InvalidStrings_ReturnsFalse()
        {
            Assert.IsFalse(FxvCliVersion.TryParse(null, out _));
            Assert.IsFalse(FxvCliVersion.TryParse("", out _));
            Assert.IsFalse(FxvCliVersion.TryParse("x.4.2", out _));
            Assert.IsFalse(FxvCliVersion.TryParse("1.y.2", out _));
            Assert.IsFalse(FxvCliVersion.TryParse("invalid", out _));
            Assert.IsFalse(FxvCliVersion.TryParse("1.2.3.4", out _));
        }

        [Test]
        public void VersionComparisons_BehaveCorrectly()
        {
            var v010 = new FxvCliVersion(0, 1, 0);
            var v060 = new FxvCliVersion(0, 6, 0);
            var v070 = new FxvCliVersion(0, 7, 0);

            var v010B = new FxvCliVersion(0, 1, 0);

            Assert.IsTrue(v010 < v060);
            Assert.IsTrue(v060 < v070);
            Assert.IsTrue(v010 <= v010B);
            Assert.IsTrue(v070 > v060);
            Assert.AreEqual(new FxvCliVersion(0, 1, 0), v010);
        }

        [Test]
        public void CheckVersion_CompatibleVersions_Succeeds()
        {
            // Lower bound of pinned [0.5.0, 0.9.0) range
            Assert.IsTrue(FlexVaultVersionGuard.CheckVersion("0.5.0", out string error1));
            Assert.IsNull(error1);

            // Within range
            Assert.IsTrue(FlexVaultVersionGuard.CheckVersion("0.7.0", out string error2));
            Assert.IsNull(error2);

            Assert.IsTrue(FlexVaultVersionGuard.CheckVersion("0.8.1", out string error3));
            Assert.IsNull(error3);
        }

        [Test]
        public void CheckVersion_IncompatibleVersions_FailsWithDescriptiveError()
        {
            // Below min bound (fxv cat required from 0.5.0 onwards)
            Assert.IsFalse(FlexVaultVersionGuard.CheckVersion("0.4.0", out string errorBelow));
            Assert.IsNotNull(errorBelow);
            StringAssert.Contains("Incompatible FlexVault CLI version", errorBelow);

            // Upper bound (exclusive 0.9.0)
            Assert.IsFalse(FlexVaultVersionGuard.CheckVersion("0.9.0", out string errorAtUpper));
            Assert.IsNotNull(errorAtUpper);
            StringAssert.Contains("Incompatible FlexVault CLI version", errorAtUpper);

            // Above upper bound
            Assert.IsFalse(FlexVaultVersionGuard.CheckVersion("1.0.0", out string errorAbove));
            Assert.IsNotNull(errorAbove);

            // Malformed
            Assert.IsFalse(FlexVaultVersionGuard.CheckVersion("x.4.2", out string errorMalformed));
            Assert.IsNotNull(errorMalformed);
            StringAssert.Contains("Invalid FlexVault CLI version string", errorMalformed);
        }

        [Test]
        public void CheckAndCacheVersion_CachesResultAndResets()
        {
            FlexVaultVersionGuard.ResetCachedVersion();
            Assert.IsNull(FlexVaultVersionGuard.IsVersionCompatible);

            bool ok = FlexVaultVersionGuard.CheckAndCacheVersion("0.8.0", out string err);
            Assert.IsTrue(ok);
            Assert.IsTrue(FlexVaultVersionGuard.IsVersionCompatible);
            Assert.AreEqual("0.8.0", FlexVaultVersionGuard.LastVersionString);
            Assert.IsNull(err);

            FlexVaultVersionGuard.ResetCachedVersion();
            Assert.IsNull(FlexVaultVersionGuard.IsVersionCompatible);
            Assert.IsNull(FlexVaultVersionGuard.LastVersionString);
        }
    }
}
