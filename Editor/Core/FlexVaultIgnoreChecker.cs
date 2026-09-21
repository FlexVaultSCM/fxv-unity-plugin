using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlexVault.VCS.Editor.UI;

namespace FlexVault.VCS.Editor.Core
{
    /// <summary>
    /// Unconditionally excludes Unity's generated folders from FlexVault tracking. Must run before
    /// anything in the plugin can trigger an auto-snapshot (see FlexVaultStateCache's static
    /// constructor), since once a path is captured into a snapshot, adding it to .fxvignore
    /// afterward no longer removes it from tracking - .fxvignore only keeps out paths that aren't
    /// tracked yet.
    /// </summary>
    internal static class FlexVaultIgnoreChecker
    {
        private static readonly string[] DefaultIgnores =
        {
            "Library/", "Temp/", "Logs/", "obj/", "Build/", "Builds/", "UserSettings/", ".vs/"
        };

        public static void EnsureDefaultIgnores()
        {
            if (!FlexVaultSettings.IsInFlexVaultRepository()) return;

            string repoRoot = FlexVaultSettings.GetRepositoryRoot();
            var missing = GetMissingEntries(repoRoot);
            if (missing.Count == 0) return;

            FlexVaultContextMenu.AppendUniqueLines(Path.Combine(repoRoot, ".fxvignore"), missing);
            UnityEngine.Debug.Log($"[FlexVault] Added {missing.Count} default ignore(s) to .fxvignore: {string.Join(", ", missing)}");
        }

        private static List<string> GetMissingEntries(string repoRoot)
        {
            string fxvignorePath = Path.Combine(repoRoot, ".fxvignore");
            var existing = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            if (File.Exists(fxvignorePath))
            {
                foreach (var line in File.ReadAllLines(fxvignorePath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0 && !trimmed.StartsWith("#"))
                    {
                        existing.Add(NormalizeEntry(trimmed));
                    }
                }
            }

            return DefaultIgnores.Where(p => !existing.Contains(NormalizeEntry(p))).ToList();
        }

        // "Library", "Library/", "Library/*" all normalize to "Library".
        private static string NormalizeEntry(string entry)
        {
            return entry.TrimEnd('/').TrimEnd('*').TrimEnd('/');
        }
    }
}
