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

        public static string ToAbsolutePath(string repoRelativePath)
        {
            if (string.IsNullOrEmpty(repoRelativePath))
            {
                return FlexVaultSettings.GetRepositoryRoot();
            }

            string repoRoot = FlexVaultSettings.GetRepositoryRoot();
            return NormalizeSeparators(Path.Combine(repoRoot, repoRelativePath));
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

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string normalized = NormalizeSeparators(path);
                string absolute = ToAbsolutePath(normalized);

                if (Directory.Exists(absolute))
                {
                    result.Add(normalized);
                    result.Add(GetCompanionMetaPath(normalized));

                    try
                    {
                        var files = Directory.GetFiles(absolute, "*", SearchOption.AllDirectories);
                        foreach (var f in files)
                        {
                            string normF = NormalizeSeparators(f);
                            result.Add(normF);
                            if (IsMetaFile(normF))
                            {
                                result.Add(GetLogicalAssetPath(normF));
                            }
                            else
                            {
                                result.Add(GetCompanionMetaPath(normF));
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
                    result.Add(normalized);

                    if (IsMetaFile(normalized))
                    {
                        string baseAsset = GetLogicalAssetPath(normalized);
                        result.Add(baseAsset);
                    }
                    else
                    {
                        string meta = GetCompanionMetaPath(normalized);
                        result.Add(meta);
                    }
                }
            }

            return new List<string>(result);
        }
    }
}
