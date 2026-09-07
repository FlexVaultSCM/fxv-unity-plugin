using System;
using System.IO;
using System.Threading.Tasks;
using FlexVault.VCS.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.UI
{
    public static class FlexVaultDiffHelper
    {
        public static async Task DiffFileAgainstBaseAsync(string repoRelativePath)
        {
            if (string.IsNullOrEmpty(repoRelativePath))
            {
                return;
            }

            string absoluteWorkingPath = FlexVaultMetaHelper.ToAbsolutePath(repoRelativePath);
            if (!File.Exists(absoluteWorkingPath))
            {
                EditorUtility.DisplayDialog("FlexVault Diff", $"Local file does not exist on disk:\n{absoluteWorkingPath}", "OK");
                return;
            }

            var status = FlexVaultStateCache.LatestStatus;
            string baseRevision = null;
            if (status?.SyncStatus?.SyncedRevision != null)
            {
                baseRevision = status.CurrentBranch != null
                    ? $"{status.CurrentBranch}.{status.SyncStatus.SyncedRevision.Value}"
                    : status.SyncStatus.SyncedRevision.Value.ToString();
            }
            else if (status?.HeadCommit?.PublishedHead?.Commit?.Revision != null)
            {
                baseRevision = status.CurrentBranch != null
                    ? $"{status.CurrentBranch}.{status.HeadCommit.PublishedHead.Commit.Revision.Value}"
                    : status.HeadCommit.PublishedHead.Commit.Revision.Value.ToString();
            }

            if (string.IsNullOrEmpty(baseRevision))
            {
                EditorUtility.DisplayDialog(
                    "FlexVault Diff",
                    $"No published base revision available to compare '{repoRelativePath}' against.\nThe file may be newly added in an unparented draft or new branch.",
                    "OK");
                return;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "FlexVaultDiff", Guid.NewGuid().ToString("N"));
            string fileName = Path.GetFileName(repoRelativePath);
            string baseFilePath = Path.Combine(tempDir, $"base_{fileName}");

            EditorUtility.DisplayProgressBar("FlexVault Diff", $"Fetching published base revision ({baseRevision})...", 0.5f);
            try
            {
                bool success = await FxvRunner.CatToFileAsync(repoRelativePath, baseRevision, baseFilePath);
                if (!success || !File.Exists(baseFilePath))
                {
                    EditorUtility.DisplayDialog(
                        "FlexVault Diff",
                        $"Could not extract base revision ({baseRevision}) for '{repoRelativePath}'.\nThe file may be newly added in this revision.",
                        "OK");
                    return;
                }

                OpenDiff(baseFilePath, absoluteWorkingPath);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("FlexVault Diff Error", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static void OpenDiff(string leftPath, string rightPath)
        {
            // Try custom diff tool configured via environment variable
            string customDiffTool = Environment.GetEnvironmentVariable("FXV_DIFF_TOOL") ?? Environment.GetEnvironmentVariable("DIFF");
            if (!string.IsNullOrEmpty(customDiffTool))
            {
                try
                {
                    var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = customDiffTool,
                        Arguments = $"\"{leftPath}\" \"{rightPath}\"",
                        UseShellExecute = true
                    });
                    if (p != null) return;
                }
                catch { }
            }

            // Try Visual Studio Code
            try
            {
                string codePath = FindExecutableOnPath("code") ?? FindExecutableOnPath("code.cmd");
                if (!string.IsNullOrEmpty(codePath))
                {
                    var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = codePath,
                        Arguments = $"--diff \"{leftPath}\" \"{rightPath}\"",
                        UseShellExecute = true
                    });
                    if (p != null) return;
                }
            }
            catch { }

            // Try JetBrains Rider
            try
            {
                string riderPath = FindExecutableOnPath("rider64.exe") ?? FindExecutableOnPath("rider");
                if (!string.IsNullOrEmpty(riderPath))
                {
                    var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = riderPath,
                        Arguments = $"diff \"{leftPath}\" \"{rightPath}\"",
                        UseShellExecute = true
                    });
                    if (p != null) return;
                }
            }
            catch { }

            // Fallback: Open both files with default system app
            EditorUtility.OpenWithDefaultApp(leftPath);
            EditorUtility.OpenWithDefaultApp(rightPath);
        }

        private static string FindExecutableOnPath(string exeName)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            char separator = Application.platform == RuntimePlatform.WindowsEditor ? ';' : ':';
            foreach (string rawPart in pathEnv.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string part = rawPart.Trim().Trim('"');
                    if (string.IsNullOrEmpty(part)) continue;

                    string candidate = Path.Combine(part, exeName);
                    if (File.Exists(candidate)) return candidate;
                    if (Application.platform == RuntimePlatform.WindowsEditor && !candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !candidate.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(candidate + ".exe")) return candidate + ".exe";
                        if (File.Exists(candidate + ".cmd")) return candidate + ".cmd";
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
