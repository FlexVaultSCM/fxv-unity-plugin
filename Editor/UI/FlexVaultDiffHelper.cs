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

            string tempDir = Path.Combine(Path.GetTempPath(), "FlexVaultDiff", Guid.NewGuid().ToString("N"));
            string fileName = Path.GetFileName(repoRelativePath);
            string baseFilePath = Path.Combine(tempDir, $"base_{fileName}");

            EditorUtility.DisplayProgressBar("FlexVault Diff", "Fetching published base revision...", 0.5f);
            try
            {
                bool success = await FxvRunner.CatToFileAsync(repoRelativePath, baseRevision, baseFilePath);
                if (!success || !File.Exists(baseFilePath) || new FileInfo(baseFilePath).Length == 0)
                {
                    EditorUtility.DisplayDialog(
                        "FlexVault Diff",
                        $"Could not extract base revision for '{repoRelativePath}'.\nThe file may be newly added or unchanged in the repository.",
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
            // If an external diff tool (e.g., TortoiseMerge, Beyond Compare, VS Code, or Rider) is configured,
            // or we can invoke Unity's code editor or open the temp file.
            // On Windows/macOS, opening the files or launching editor diff:
            try
            {
                // In Unity Editor, try CodeEditor or OpenWithDefaultApp
                // If VS Code is present, `code --diff <left> <right>` is widely supported
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
            catch
            {
                // Fall back
            }

            // Fallback: Open both files with default app or show in explorer
            EditorUtility.OpenWithDefaultApp(leftPath);
            EditorUtility.OpenWithDefaultApp(rightPath);
        }

        private static string FindExecutableOnPath(string exeName)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            char separator = Application.platform == RuntimePlatform.WindowsEditor ? ';' : ':';
            foreach (string part in pathEnv.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(part.Trim(), exeName);
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
