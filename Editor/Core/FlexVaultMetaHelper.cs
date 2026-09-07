using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FlexVault.VCS.Editor.Core
{
    public static class FlexVaultMetaHelper
    {
        public static string NormalizeSeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }
            return path.Replace('\\', '/').TrimEnd('/');
        }

        public static string ToRepoRelativePath(string projectOrAbsolutePath)
        {
            if (string.IsNullOrEmpty(projectOrAbsolutePath))
            {
                return string.Empty;
            }

            string repoRoot = FlexVaultSettings.GetRepositoryRoot();
            string normalized = NormalizeSeparators(projectOrAbsolutePath);

            if (!Path.IsPathRooted(normalized))
            {
                string projectRoot = NormalizeSeparators(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
                normalized = NormalizeSeparators(Path.Combine(projectRoot, normalized));
            }

            string repoRootNormalized = NormalizeSeparators(repoRoot);
            if (normalized.Equals(repoRootNormalized, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (normalized.StartsWith(repoRootNormalized + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(repoRootNormalized.Length + 1);
            }

            return normalized;
        }

        public static string ToAbsolutePath(string projectOrRepoRelativePath)
        {
            if (string.IsNullOrEmpty(projectOrRepoRelativePath))
            {
                return FlexVaultSettings.GetRepositoryRoot();
            }

            string normalized = NormalizeSeparators(projectOrRepoRelativePath);
            if (Path.IsPathRooted(normalized))
            {
                return normalized;
            }

            string projectRoot = NormalizeSeparators(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            string fullProjectPath = NormalizeSeparators(Path.Combine(projectRoot, normalized));
            if (File.Exists(fullProjectPath) || Directory.Exists(fullProjectPath) || File.Exists(fullProjectPath + ".meta"))
            {
                return fullProjectPath;
            }

            string repoRoot = FlexVaultSettings.GetRepositoryRoot();
            string fullRepoPath = NormalizeSeparators(Path.Combine(repoRoot, normalized));
            if (File.Exists(fullRepoPath) || Directory.Exists(fullRepoPath) || File.Exists(fullRepoPath + ".meta"))
            {
                return fullRepoPath;
            }

            if (normalized.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Packages", StringComparison.OrdinalIgnoreCase))
            {
                return fullProjectPath;
            }

            return fullRepoPath;
        }

        public static string ToProjectRelativePath(string repoRelativePath)
        {
            string absolute = ToAbsolutePath(repoRelativePath);
            string projectRoot = NormalizeSeparators(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));

            if (absolute.Equals(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (absolute.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return absolute.Substring(projectRoot.Length + 1);
            }

            return absolute;
        }

        public static bool IsMetaFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            return path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetCompanionMetaPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }

            if (IsMetaFile(assetPath))
            {
                return assetPath;
            }

            return assetPath + ".meta";
        }

        public static string GetLogicalAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            if (IsMetaFile(path))
            {
                return path.Substring(0, path.Length - ".meta".Length);
            }

            return path;
        }

        public static List<string> ExpandWithMeta(IEnumerable<string> paths)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (paths == null)
            {
                return new List<string>();
            }

            foreach (string rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                string absolute = ToAbsolutePath(rawPath);
                string repoRelative = ToRepoRelativePath(absolute);

                if (Directory.Exists(absolute))
                {
                    // Directories themselves cannot be reverted directly by fxv CLI (reverting directories is rejected).
                    // Include the directory's companion .meta file and all descendant files and their .meta files.
                    string folderMeta = GetCompanionMetaPath(repoRelative);
                    if (!string.IsNullOrEmpty(folderMeta))
                    {
                        result.Add(folderMeta);
                    }

                    try
                    {
                        var dirs = Directory.GetDirectories(absolute, "*", SearchOption.AllDirectories);
                        foreach (var d in dirs)
                        {
                            string dirRepoRel = ToRepoRelativePath(d);
                            string subFolderMeta = GetCompanionMetaPath(dirRepoRel);
                            if (!string.IsNullOrEmpty(subFolderMeta))
                            {
                                result.Add(subFolderMeta);
                            }
                        }

                        var files = Directory.GetFiles(absolute, "*", SearchOption.AllDirectories);
                        foreach (var f in files)
                        {
                            string fileRepoRel = ToRepoRelativePath(f);
                            result.Add(fileRepoRel);
                            if (IsMetaFile(fileRepoRel))
                            {
                                result.Add(GetLogicalAssetPath(fileRepoRel));
                            }
                            else
                            {
                                result.Add(GetCompanionMetaPath(fileRepoRel));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FlexVault] Error traversing directory '{absolute}': {ex.Message}");
                    }
                }
                else
                {
                    result.Add(repoRelative);

                    if (IsMetaFile(repoRelative))
                    {
                        string baseAsset = GetLogicalAssetPath(repoRelative);
                        result.Add(baseAsset);
                    }
                    else
                    {
                        string meta = GetCompanionMetaPath(repoRelative);
                        result.Add(meta);
                    }
                }
            }

            return new List<string>(result);
        }
    }
}
