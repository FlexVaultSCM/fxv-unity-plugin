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
            string repoRootNormalized = NormalizeSeparators(repoRoot);
            string normalized = NormalizeSeparators(projectOrAbsolutePath);

            if (!Path.IsPathRooted(normalized))
            {
                // If it already resolves directly relative to the repository root, keep it as repo-relative
                if (!string.IsNullOrEmpty(repoRootNormalized))
                {
                    string candidateRepoPath = NormalizeSeparators(Path.Combine(repoRootNormalized, normalized));
                    if (File.Exists(candidateRepoPath) || Directory.Exists(candidateRepoPath) || File.Exists(candidateRepoPath + ".meta"))
                    {
                        return normalized;
                    }
                }

                string projectRoot = NormalizeSeparators(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
                normalized = NormalizeSeparators(Path.Combine(projectRoot, normalized));
            }

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

        private static string s_cachedProjectRoot;
        public static string ProjectRoot => s_cachedProjectRoot ?? (s_cachedProjectRoot = NormalizeSeparators(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))));

        public static void InvalidateProjectRoot()
        {
            s_cachedProjectRoot = null;
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

            string projectRoot = ProjectRoot;
            string fullProjectPath = NormalizeSeparators(Path.Combine(projectRoot, normalized));

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return fullProjectPath;
            }

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

            return fullRepoPath;
        }

        public static string ToProjectRelativePath(string repoRelativePath)
        {
            if (string.IsNullOrEmpty(repoRelativePath)) return string.Empty;

            string normalized = NormalizeSeparators(repoRelativePath);
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            string absolute = ToAbsolutePath(repoRelativePath);
            string projectRoot = ProjectRoot;

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

                    SafeEnumerateDirectory(absolute, result);
                }
                else
                {
                    // Check if this path represents a deleted directory tracked by FlexVault.
                    // The CLI tracks deleted files inside the directory, but rejects reverting bare directory paths.
                    string folderPrefix = NormalizeSeparators(repoRelative).TrimEnd('/') + "/";
                    bool isDeletedFolder = false;
                    var status = FlexVaultStateCache.LatestStatus;
                    if (status?.Files != null)
                    {
                        foreach (var file in status.Files)
                        {
                            if (!string.IsNullOrEmpty(file?.Path) && NormalizeSeparators(file.Path).StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                isDeletedFolder = true;
                                result.Add(file.Path);
                                string companion = GetCompanionMetaPath(file.Path);
                                if (!string.IsNullOrEmpty(companion))
                                {
                                    result.Add(companion);
                                }
                            }
                        }
                    }

                    if (isDeletedFolder)
                    {
                        string folderMeta = GetCompanionMetaPath(repoRelative);
                        if (!string.IsNullOrEmpty(folderMeta))
                        {
                            result.Add(folderMeta);
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
            }

            return new List<string>(result);
        }

        private static void SafeEnumerateDirectory(string rootDir, HashSet<string> result)
        {
            var queue = new Queue<string>();
            queue.Enqueue(rootDir);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();

                try
                {
                    string[] subDirs = Directory.GetDirectories(current);
                    foreach (var d in subDirs)
                    {
                        string dirRepoRel = ToRepoRelativePath(d);
                        string subFolderMeta = GetCompanionMetaPath(dirRepoRel);
                        if (!string.IsNullOrEmpty(subFolderMeta))
                        {
                            result.Add(subFolderMeta);
                        }
                        queue.Enqueue(d);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FlexVault] Error traversing subdirectories of '{current}': {ex.Message}");
                }

                try
                {
                    string[] files = Directory.GetFiles(current);
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
                    Debug.LogWarning($"[FlexVault] Error traversing files in '{current}': {ex.Message}");
                }
            }
        }

        public static void PingAsset(string repoOrProjectPath)
        {
            if (string.IsNullOrEmpty(repoOrProjectPath)) return;

            string projectRelative = ToProjectRelativePath(repoOrProjectPath);
            if (IsMetaFile(projectRelative))
            {
                projectRelative = GetLogicalAssetPath(projectRelative);
            }

            var obj = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(projectRelative);
            if (obj != null)
            {
                UnityEditor.Selection.activeObject = obj;
                UnityEditor.EditorGUIUtility.PingObject(obj);
            }
            else
            {
                // Fallback for non-Asset files (e.g. ProjectSettings)
                string absolute = ToAbsolutePath(repoOrProjectPath);
                if (System.IO.File.Exists(absolute) || System.IO.Directory.Exists(absolute))
                {
                    UnityEditor.EditorUtility.RevealInFinder(absolute);
                }
            }
        }
    }
}
