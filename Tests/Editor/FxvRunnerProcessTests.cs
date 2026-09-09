using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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
        public void FormatArguments_ArgsWithTrailingBackslash_EscapesBackslashBeforeQuote()
        {
            var args = new[] { "revert", @"C:\My Project\Folder\" };
            string formatted = InvokeFormatArguments(args);
            Assert.AreEqual(@"revert ""C:\My Project\Folder\\""", formatted);
        }

        [Test]
        public void FormatArguments_ArgsWithTrailingMultipleBackslashes_DoublesAllBackslashes()
        {
            var args = new[] { "revert", @"C:\My Project\Folder\\" };
            string formatted = InvokeFormatArguments(args);
            Assert.AreEqual(@"revert ""C:\My Project\Folder\\\\""", formatted);
        }

        [Test]
        public async Task RunCommandAsync_NonExistentBinary_FailsGracefully()
        {
            string invalidBinary = Path.Combine(Path.GetTempPath(), "nonexistent_fxv_dir", "definitely_not_a_binary");

            // Ensure it does not throw an unhandled exception, but returns Success == false
            var result = await FxvRunner.RunCommandAsync<StatusPayload>(new[] { "status" }, customBinaryPath: invalidBinary);

            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.ErrorMessage);
        }

        [Test]
        public async Task CatToFileAsync_NonExistentBinary_ReturnsFalseCleanly()
        {
            string invalidBinary = Path.Combine(Path.GetTempPath(), "nonexistent_fxv_dir", "definitely_not_a_binary");
            string tempTarget = Path.Combine(Path.GetTempPath(), "target_" + Guid.NewGuid().ToString("N") + ".txt");
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("CatToFileAsync failed"));
            try
            {
                bool success = await FxvRunner.CatToFileAsync("Assets/Test.cs", "main.1", tempTarget, customBinaryPath: invalidBinary);
                Assert.IsFalse(success);
                Assert.IsFalse(File.Exists(tempTarget));
            }
            finally
            {
                if (File.Exists(tempTarget))
                {
                    File.Delete(tempTarget);
                }
            }
        }
    }
}
