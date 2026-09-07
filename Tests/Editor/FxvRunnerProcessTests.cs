using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using FlexVault.VCS.Editor.Core;

namespace FlexVault.VCS.Editor.Tests
{
    [TestFixture]
    public class FxvRunnerProcessTests
    {
        private static string InvokeFormatArguments(IEnumerable<string> args)
        {
            var method = typeof(FxvRunner).GetMethod("FormatArguments", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "FormatArguments method should exist on FxvRunner");
            return (string)method.Invoke(null, new object[] { args });
        }

        [Test]
        public void FormatArguments_PlainArgs_JoinedWithSpaces()
        {
            var args = new[] { "status", "--unattended", "--no-color" };
            string formatted = InvokeFormatArguments(args);
            Assert.AreEqual("status --unattended --no-color", formatted);
        }

        [Test]
        public void FormatArguments_ArgsWithSpaces_WrappedInQuotes()
        {
            var args = new[] { "snapshot", "-d", "My Commit Message" };
            string formatted = InvokeFormatArguments(args);
            Assert.AreEqual("snapshot -d \"My Commit Message\"", formatted);
        }

        [Test]
        public void FormatArguments_ArgsWithEmbeddedQuotes_EscapedProperly()
        {
            var args = new[] { "snapshot", "-d", "Fix \"jump\" bug" };
            string formatted = InvokeFormatArguments(args);
            Assert.AreEqual("snapshot -d \"Fix \\\"jump\\\" bug\"", formatted);
        }

        [Test]
        public void FormatArguments_EmptyStringArg_ProducesEmptyQuotes()
        {
            var args = new[] { "commit", "" };
            string formatted = InvokeFormatArguments(args);
            Assert.AreEqual("commit \"\"", formatted);
        }

        [Test]
        public async Task RunCommandAsync_NonExistentBinary_FailsGracefully()
        {
            string originalCustom = FlexVaultSettings.CustomBinaryPath;
            try
            {
                // Force an unlaunchable binary path
                FlexVaultSettings.CustomBinaryPath = @"C:\NonexistentDir\definitely_not_a_binary.exe";

                // Ensure it does not throw an unhandled exception, but returns Success == false
                var result = await FxvRunner.RunCommandAsync<StatusPayload>(new[] { "status" });

                Assert.IsFalse(result.Success);
                Assert.IsNotEmpty(result.ErrorMessage);
            }
            finally
            {
                FlexVaultSettings.CustomBinaryPath = originalCustom;
            }
        }

        [Test]
        public async Task CatToFileAsync_NonExistentBinary_ReturnsFalseCleanly()
        {
            string originalCustom = FlexVaultSettings.CustomBinaryPath;
            string tempTarget = Path.Combine(Path.GetTempPath(), "target_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                FlexVaultSettings.CustomBinaryPath = @"C:\NonexistentDir\definitely_not_a_binary.exe";

                bool success = await FxvRunner.CatToFileAsync("Assets/Test.cs", "main.1", tempTarget);
                Assert.IsFalse(success);
                Assert.IsFalse(File.Exists(tempTarget));
            }
            finally
            {
                FlexVaultSettings.CustomBinaryPath = originalCustom;
                if (File.Exists(tempTarget))
                {
                    File.Delete(tempTarget);
                }
            }
        }
    }
}
