using System;
using System.Collections.Generic;
using FlexVault.VCS.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.UI
{
    public class FlexVaultHistoryWindow : EditorWindow
    {
        private Vector2 m_scrollPos;
        private List<CommitRefJson> m_entries = new List<CommitRefJson>();
        private bool m_isLoading;
        private string m_filterPath = string.Empty;
        private string m_statusMessage = string.Empty;

        public static void ShowHistory(string targetPath = null)
        {
            var window = GetWindow<FlexVaultHistoryWindow>("FlexVault History");
            window.minSize = new Vector2(500, 400);
            window.m_filterPath = targetPath ?? string.Empty;
            window.LoadHistory();
            window.Show();
        }

        private async void LoadHistory()
        {
            if (!FlexVaultSettings.IsInFlexVaultRepository())
            {
                m_statusMessage = "No FlexVault repository detected.";
                return;
            }

            m_isLoading = true;
            m_statusMessage = "Loading revision history...";
            Repaint();

            try
            {
                var result = await FxvRunner.GetHistoryAsync(count: 50);
                if (result.Success && result.Data != null)
                {
                    m_entries = result.Data.Entries ?? new List<CommitRefJson>();
                    m_statusMessage = m_entries.Count == 0 ? "No history entries found." : string.Empty;
                }
                else
                {
                    m_statusMessage = $"Failed to load history: {result.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                m_statusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                m_isLoading = false;
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                if (!string.IsNullOrEmpty(m_filterPath))
                {
                    GUILayout.Label($"Target: {m_filterPath}", EditorStyles.miniLabel);
                }
                else
                {
                    GUILayout.Label("Branch History", EditorStyles.boldLabel);
                }

                GUILayout.FlexibleSpace();

                GUI.enabled = !m_isLoading;
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(65)))
                {
                    LoadHistory();
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();

            if (m_isLoading)
            {
                EditorGUILayout.HelpBox("Loading revision history from FlexVault...", MessageType.Info);
                return;
            }

            if (!string.IsNullOrEmpty(m_statusMessage))
            {
                EditorGUILayout.HelpBox(m_statusMessage, MessageType.Info);
            }

            m_scrollPos = EditorGUILayout.BeginScrollView(m_scrollPos, GUILayout.ExpandHeight(true));
            {
                for (int i = 0; i < m_entries.Count; i++)
                {
                    var entry = m_entries[i];
                    DrawHistoryEntry(entry, i);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawHistoryEntry(CommitRefJson entry, int index)
        {
            var bgStyle = (index % 2 == 0) ? EditorStyles.helpBox : EditorStyles.textArea;
            EditorGUILayout.BeginVertical(bgStyle);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    bool isPublished = entry.Commit?.Type == "published";
                    Color badgeColor = isPublished ? new Color(0.2f, 0.6f, 1f) : new Color(0.85f, 0.5f, 0.1f);
                    string typeLabel = isPublished ? "[Published]" : "[Draft]";

                    var style = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        normal = { textColor = badgeColor }
                    };
                    GUILayout.Label(typeLabel, style, GUILayout.Width(80));

                    GUILayout.Label(entry.RevisionDisplay, EditorStyles.boldLabel, GUILayout.Width(130));

                    string author = !string.IsNullOrEmpty(entry.AuthorDisplayName)
                        ? entry.AuthorDisplayName
                        : (!string.IsNullOrEmpty(entry.AuthorId) ? entry.AuthorId : "Unknown");
                    GUILayout.Label(author, EditorStyles.miniLabel, GUILayout.Width(120));

                    string timeStr = entry.TimestampMillisSinceEpochUtc > 0
                        ? entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        : string.Empty;
                    GUILayout.Label(timeStr, EditorStyles.miniLabel, GUILayout.Width(110));

                    GUILayout.FlexibleSpace();

                    if (!string.IsNullOrEmpty(m_filterPath))
                    {
                        if (GUILayout.Button("Diff This Rev", EditorStyles.miniButton, GUILayout.Width(90)))
                        {
                            DiffWithRevision(m_filterPath, entry.RevisionDisplay);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                string desc = !string.IsNullOrWhiteSpace(entry.Description) ? entry.Description.Trim() : "(No description)";
                EditorGUILayout.LabelField(desc, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(2f);
        }

        private async void DiffWithRevision(string repoRelativePath, string revision)
        {
            string absolute = FlexVaultMetaHelper.ToAbsolutePath(repoRelativePath);
            if (!System.IO.File.Exists(absolute))
            {
                EditorUtility.DisplayDialog("FlexVault Diff", $"Local file does not exist:\n{absolute}", "OK");
                return;
            }

            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FlexVaultDiff", Guid.NewGuid().ToString("N"));
            string revFile = System.IO.Path.Combine(tempDir, $"{revision}_{System.IO.Path.GetFileName(repoRelativePath)}");

            EditorUtility.DisplayProgressBar("FlexVault Diff", $"Extracting revision {revision}...", 0.5f);
            try
            {
                bool success = await FxvRunner.CatToFileAsync(repoRelativePath, revision, revFile);
                if (success && System.IO.File.Exists(revFile))
                {
                    FlexVaultDiffHelper.OpenDiff(revFile, absolute);
                }
                else
                {
                    EditorUtility.DisplayDialog("FlexVault Diff", $"Could not extract revision {revision} for '{repoRelativePath}'.", "OK");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
