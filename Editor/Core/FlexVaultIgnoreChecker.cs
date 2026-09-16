using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlexVault.VCS.Editor.UI;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.Core
{
    /// <summary>
    /// Prompts once per editor startup to exclude Unity's generated folders from FlexVault tracking.
    /// </summary>
    internal static class FlexVaultIgnoreChecker
    {
        private static readonly string[] DefaultIgnores =
        {
            "Library/", "Temp/", "Logs/", "obj/", "Build/", "Builds/", "UserSettings/", ".vs/"
        };

        private const string DismissedPrefKeyPrefix = "FlexVault.IgnorePrompt.Dismissed.";

        public static void CheckAndPromptOnStartup()
        {
            string repoRoot = FlexVaultSettings.GetRepositoryRoot();
            if (string.IsNullOrEmpty(repoRoot)) return;

            var missing = GetMissingEntries(repoRoot);
            if (missing.Count == 0) return;

            string dismissedKey = DismissedPrefKeyPrefix + repoRoot.GetHashCode();
            var dismissed = new HashSet<string>(
                EditorPrefs.GetString(dismissedKey, string.Empty).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

            var toPrompt = missing.Where(m => !dismissed.Contains(m)).ToList();
            if (toPrompt.Count == 0) return;

            bool add = EditorUtility.DisplayDialog(
                "FlexVault",
                $"Exclude Unity's generated folders from tracking?\n\n{string.Join("\n", toPrompt)}",
                "Add to .fxvignore",
                "Not Now");

            if (add)
            {
                FlexVaultContextMenu.AppendUniqueLines(Path.Combine(repoRoot, ".fxvignore"), toPrompt);
                Debug.Log($"[FlexVault] Added {toPrompt.Count} default ignore(s) to .fxvignore");
            }
            else
            {
                dismissed.UnionWith(toPrompt);
                EditorPrefs.SetString(dismissedKey, string.Join("|", dismissed));
            }
        }

        private static List<string> GetMissingEntries(string repoRoot)
        {
            string fxvignorePath = Path.Combine(repoRoot, ".fxvignore");
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(fxvignorePath))
            {
                foreach (var line in File.ReadAllLines(fxvignorePath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0 && !trimmed.StartsWith("#"))
                    {
                        existing.Add(trimmed);
                    }
                }
            }

            return DefaultIgnores.Where(p => !existing.Contains(p)).ToList();
        }
    }
}
