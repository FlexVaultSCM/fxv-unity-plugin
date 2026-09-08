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
        public static async Task DiffFileAgainstBaseAsync(string repoRelativePath, string explicitBaseRevision = null)
        {
            if (string.IsNullOrEmpty(repoRelativePath))
            {
                return;
            }

            string baseRevision = !string.IsNullOrEmpty(explicitBaseRevision)
                ? explicitBaseRevision
                : GetBaseRevisionForFile(repoRelativePath);

            if (string.IsNullOrEmpty(baseRevision))
            {
                EditorUtility.DisplayDialog(
                    "FlexVault Diff",
                    $"No base revision available to compare '{repoRelativePath}' against.\nThe file may be newly added with no prior snapshot or published revisions.",
                    "OK");
                return;
            }

            await DiffWithWorkingCopyAsync(repoRelativePath, baseRevision);
        }

        public static string GetBaseRevisionForFile(string repoRelativePath)
        {
            var status = FlexVaultStateCache.LatestStatus;
            if (status == null) return null;

            var fileItem = System.Linq.Enumerable.FirstOrDefault(
                status.Files,
                f => string.Equals(f.Path, repoRelativePath, StringComparison.OrdinalIgnoreCase));

            // 1. If file has pending workspace modifications, diff against the latest local snapshot or published HEAD
            if (fileItem != null && fileItem.NeedsSnapshot)
            {
                if (status.HeadCommit?.LocalSnapshot != null)
                {
                    return status.HeadCommit.LocalSnapshot.RevisionDisplay;
                }
                if (status.HeadCommit?.PublishedHead != null)
                {
                    return status.HeadCommit.PublishedHead.RevisionDisplay;
                }
                if (status.SyncStatus?.SyncedRevision != null)
                {
                    return status.CurrentBranch != null
                        ? $"{status.CurrentBranch}.{status.SyncStatus.SyncedRevision.Value}"
                        : status.SyncStatus.SyncedRevision.Value.ToString();
                }
            }

            // 2. If file has unpublished changes (or general base diff), prefer the published remote base
            if (status.SyncStatus?.SyncedRevision != null)
            {
                return status.CurrentBranch != null
                    ? $"{status.CurrentBranch}.{status.SyncStatus.SyncedRevision.Value}"
                    : status.SyncStatus.SyncedRevision.Value.ToString();
            }
            if (status.HeadCommit?.PublishedHead != null)
            {
                return status.HeadCommit.PublishedHead.RevisionDisplay;
            }

            // 3. Fall back to local snapshot if no published revision exists (unparented draft or new branch)
            if (status.HeadCommit?.LocalSnapshot != null)
            {
                return status.HeadCommit.LocalSnapshot.RevisionDisplay;
            }

            return null;
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

        public static async Task DiffWithWorkingCopyAsync(string repoRelativePath, string revision)
        {
            string absolute = FlexVaultMetaHelper.ToAbsolutePath(repoRelativePath);
            if (!File.Exists(absolute))
            {
                EditorUtility.DisplayDialog("FlexVault Diff", $"Local file does not exist:\n{absolute}", "OK");
                return;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "FlexVaultDiff", Guid.NewGuid().ToString("N"));
            string safeRevName = revision.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            string revFile = Path.Combine(tempDir, $"{safeRevName}_{Path.GetFileName(repoRelativePath)}");

            EditorUtility.DisplayProgressBar("FlexVault Diff", $"Extracting revision {revision}...", 0.5f);
            try
            {
                bool success = await FxvRunner.CatToFileAsync(repoRelativePath, revision, revFile);
                if (success && File.Exists(revFile))
                {
                    if (FilesAreEqual(revFile, absolute))
                    {
                        EditorUtility.DisplayDialog(
                            "FlexVault Diff",
                            $"No differences found.\n\nThe working copy '{repoRelativePath}' is identical to revision {revision}.",
                            "OK");
                        return;
                    }

                    OpenDiff(revFile, absolute);
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "FlexVault Diff",
                        $"Could not extract revision {revision} for '{repoRelativePath}'.\nThe file may not have existed in that revision.",
                        "OK");
                }
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

        public static async Task DiffTwoRevisionsAsync(string repoRelativePath, string olderRev, string newerRev)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FlexVaultDiff", Guid.NewGuid().ToString("N"));
            string fileName = Path.GetFileName(repoRelativePath);
            string safeOlder = olderRev.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            string safeNewer = newerRev.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            string olderFile = Path.Combine(tempDir, $"{safeOlder}_{fileName}");
            string newerFile = Path.Combine(tempDir, $"{safeNewer}_{fileName}");

            EditorUtility.DisplayProgressBar("FlexVault Diff", $"Extracting revisions {olderRev} and {newerRev}...", 0.3f);
            try
            {
                bool oldSuccess = await FxvRunner.CatToFileAsync(repoRelativePath, olderRev, olderFile);
                if (!oldSuccess || !File.Exists(olderFile))
                {
                    EditorUtility.DisplayDialog(
                        "FlexVault Diff",
                        $"Could not extract older revision {olderRev} for '{repoRelativePath}'.\nThe file may have been added in a later revision.",
                        "OK");
                    return;
                }

                EditorUtility.DisplayProgressBar("FlexVault Diff", $"Extracting revision {newerRev}...", 0.7f);
                bool newSuccess = await FxvRunner.CatToFileAsync(repoRelativePath, newerRev, newerFile);
                if (!newSuccess || !File.Exists(newerFile))
                {
                    EditorUtility.DisplayDialog("FlexVault Diff", $"Could not extract revision {newerRev} for '{repoRelativePath}'.", "OK");
                    return;
                }

                if (FilesAreEqual(olderFile, newerFile))
                {
                    EditorUtility.DisplayDialog(
                        "FlexVault Diff",
                        $"No differences found.\n\n'{repoRelativePath}' is identical between {olderRev} and {newerRev}.",
                        "OK");
                    return;
                }

                OpenDiff(olderFile, newerFile);
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

        public static bool FilesAreEqual(string path1, string path2)
        {
            try
            {
                var fi1 = new FileInfo(path1);
                var fi2 = new FileInfo(path2);
                if (fi1.Length != fi2.Length)
                {
                    return false;
                }

                byte[] b1 = File.ReadAllBytes(path1);
                byte[] b2 = File.ReadAllBytes(path2);
                if (b1.Length != b2.Length) return false;
                for (int i = 0; i < b1.Length; i++)
                {
                    if (b1[i] != b2[i]) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
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
