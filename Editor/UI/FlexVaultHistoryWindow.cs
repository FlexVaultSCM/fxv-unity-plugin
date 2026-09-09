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

        private void OnEnable()
        {
            FlexVaultStateCache.OnStateChanged += OnStateChanged;
            if (FlexVaultSettings.IsInFlexVaultRepository() && FlexVaultStateCache.LatestStatus == null)
            {
                FlexVaultStateCache.RefreshAsync();
            }
        }

        private void OnDisable()
        {
            FlexVaultStateCache.OnStateChanged -= OnStateChanged;
        }

        private void OnFocus()
        {
            if (FlexVaultSettings.IsInFlexVaultRepository() && FlexVaultStateCache.LatestStatus == null)
            {
                FlexVaultStateCache.RefreshAsync();
            }
        }

        private void OnStateChanged()
        {
            Repaint();
        }

        private async void LoadHistory()
        {
            if (!FlexVaultSettings.IsInFlexVaultRepository())
            {
                m_statusMessage = "No FlexVault repository detected.";
                return;
            }

            if (FlexVaultStateCache.LatestStatus == null)
            {
                FlexVaultStateCache.RefreshAsync();
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

        private readonly HashSet<string> m_expandedRevisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ChangeInfoPayload> m_changeInfoCache = new Dictionary<string, ChangeInfoPayload>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> m_loadingChangeInfo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void DrawHistoryEntry(CommitRefJson entry, int index)
        {
            bool isCurrent = FlexVaultStateCache.IsCurrentWorkspaceRevision(entry, m_entries);
            var prevBg = GUI.backgroundColor;
            if (isCurrent)
            {
                GUI.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.20f, 0.45f, 0.28f, 1f)
                    : new Color(0.72f, 0.92f, 0.78f, 1f);
            }
            var bgStyle = (index % 2 == 0) ? EditorStyles.helpBox : EditorStyles.textArea;
            EditorGUILayout.BeginVertical(bgStyle);

            {
                EditorGUILayout.BeginHorizontal();
                {
                    string rev = entry.RevisionDisplay;
                    bool isExpanded = m_expandedRevisions.Contains(rev);
                    string toggleSymbol = isExpanded ? "▼" : "▶";
                    if (GUILayout.Button(toggleSymbol, EditorStyles.label, GUILayout.Width(18)))
                    {
                        if (isExpanded)
                        {
                            m_expandedRevisions.Remove(rev);
                        }
                        else
                        {
                            m_expandedRevisions.Add(rev);
                            EnsureChangeInfoLoaded(rev);
                        }
                    }

                    if (isCurrent)
                    {
                        Color prevCol2 = GUI.contentColor;
                        GUI.contentColor = EditorGUIUtility.isProSkin ? new Color(0.3f, 1f, 0.5f) : new Color(0.1f, 0.6f, 0.2f);
                        GUILayout.Label("● Current", EditorStyles.boldLabel, GUILayout.Width(72));
                        GUI.contentColor = prevCol2;
                    }

                    bool isPublished = entry.Commit?.Type == "published";
                    Color badgeColor = isPublished ? new Color(0.2f, 0.6f, 1f) : new Color(0.85f, 0.5f, 0.1f);
                    string typeLabel = isPublished ? "[Published]" : "[Draft]";

                    Color prevCol = GUI.contentColor;
                    GUI.contentColor = badgeColor;
                    GUILayout.Label(typeLabel, EditorStyles.miniBoldLabel, GUILayout.Width(75));
                    GUI.contentColor = prevCol;

                    GUILayout.Label(rev, EditorStyles.boldLabel, GUILayout.Width(115));

                    string author = !string.IsNullOrEmpty(entry.AuthorDisplayName)
                        ? entry.AuthorDisplayName
                        : (!string.IsNullOrEmpty(entry.AuthorId) ? entry.AuthorId : "Unknown");
                    GUILayout.Label(author, EditorStyles.miniLabel, GUILayout.Width(110));

                    string timeStr = entry.TimestampMillisSinceEpochUtc > 0
                        ? entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        : string.Empty;
                    GUILayout.Label(timeStr, EditorStyles.miniLabel, GUILayout.Width(110));

                    GUILayout.FlexibleSpace();

                    if (!string.IsNullOrEmpty(m_filterPath))
                    {
                        if (GUILayout.Button("Diff vs Current", EditorStyles.miniButton, GUILayout.Width(95)))
                        {
                            DiffWithWorkingCopy(m_filterPath, entry.RevisionDisplay);
                        }

                        // In history, m_entries is in reverse chronological order (newest first).
                        // The revision immediately preceding this one is at index + 1.
                        if (index + 1 < m_entries.Count)
                        {
                            var prevEntry = m_entries[index + 1];
                            if (GUILayout.Button("Diff vs Prev", EditorStyles.miniButton, GUILayout.Width(85)))
                            {
                                DiffTwoRevisions(m_filterPath, prevEntry.RevisionDisplay, entry.RevisionDisplay);
                            }
                        }
                    }
                    else
                    {
                        GUI.enabled = !isCurrent;
                        if (GUILayout.Button(isCurrent ? "Current" : "Go To", EditorStyles.miniButton, GUILayout.Width(65)))
                        {
                            FlexVaultWindow.ExecuteGotoRevision(entry.RevisionDisplay, Repaint);
                        }
                        GUI.enabled = true;
                    }
                }
                EditorGUILayout.EndHorizontal();

                string desc = !string.IsNullOrWhiteSpace(entry.Description) ? entry.Description.Trim() : "(No description)";
                EditorGUILayout.LabelField(desc, EditorStyles.wordWrappedLabel);

                if (m_expandedRevisions.Contains(entry.RevisionDisplay))
                {
                    DrawExpandedChanges(entry, index);
                }
            }
            EditorGUILayout.EndVertical();
            GUI.backgroundColor = prevBg;
            GUILayout.Space(2f);
        }

        private async void EnsureChangeInfoLoaded(string revision)
        {
            if (m_changeInfoCache.ContainsKey(revision) || m_loadingChangeInfo.Contains(revision))
            {
                return;
            }

            m_loadingChangeInfo.Add(revision);
            try
            {
                var result = await FxvRunner.GetChangeInfoAsync(revision);
                if (result.Success && result.Data != null)
                {
                    m_changeInfoCache[revision] = result.Data;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FlexVault] Failed to load change details for {revision}: {ex.Message}");
            }
            finally
            {
                m_loadingChangeInfo.Remove(revision);
                Repaint();
            }
        }

        private void DrawExpandedChanges(CommitRefJson entry, int commitIndex)
        {
            string rev = entry.RevisionDisplay;
            if (m_loadingChangeInfo.Contains(rev))
            {
                EditorGUILayout.LabelField("Loading changed files...", EditorStyles.miniLabel);
                return;
            }

            if (!m_changeInfoCache.TryGetValue(rev, out var info) || info.Changes == null || info.Changes.Count == 0)
            {
                EditorGUILayout.LabelField("No changed files recorded in this revision.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField(
                        $"Changed Files ({info.Changes.Count}): +{info.Summary?.Added ?? 0} ~{info.Summary?.Modified ?? 0} -{info.Summary?.Deleted ?? 0}",
                        EditorStyles.miniBoldLabel);
                    GUILayout.FlexibleSpace();
                }
                EditorGUILayout.EndHorizontal();

                foreach (var file in info.Changes)
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        DrawActionBadge(file.Action);

                        if (GUILayout.Button(file.Path, EditorStyles.linkLabel))
                        {
                            FlexVaultMetaHelper.PingAsset(file.Path);
                        }

                        GUILayout.FlexibleSpace();

                        if (!string.Equals(file.Action, "deleted", StringComparison.OrdinalIgnoreCase))
                        {
                            if (GUILayout.Button("Diff vs Current", EditorStyles.miniButton, GUILayout.Width(90)))
                            {
                                DiffWithWorkingCopy(file.Path, rev);
                            }

                            if (commitIndex + 1 < m_entries.Count)
                            {
                                var prev = m_entries[commitIndex + 1];
                                if (GUILayout.Button("Diff vs Prev", EditorStyles.miniButton, GUILayout.Width(80)))
                                {
                                    DiffTwoRevisions(file.Path, prev.RevisionDisplay, rev);
                                }
                            }
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawActionBadge(string action)
        {
            Color color;
            string text;

            switch (action?.ToLowerInvariant())
            {
                case "added":
                    color = new Color(0.2f, 0.7f, 0.3f);
                    text = "[+]";
                    break;
                case "modified":
                    color = new Color(0.2f, 0.5f, 0.9f);
                    text = "[~]";
                    break;
                case "deleted":
                    color = new Color(0.9f, 0.2f, 0.2f);
                    text = "[-]";
                    break;
                default:
                    color = Color.gray;
                    text = "[?]";
                    break;
            }

            Color prevCol = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(text, EditorStyles.miniBoldLabel, GUILayout.Width(22));
            GUI.contentColor = prevCol;
        }

        private async void DiffWithWorkingCopy(string repoRelativePath, string revision)
        {
            await FlexVaultDiffHelper.DiffWithWorkingCopyAsync(repoRelativePath, revision);
        }

        private async void DiffTwoRevisions(string repoRelativePath, string olderRev, string newerRev)
        {
            await FlexVaultDiffHelper.DiffTwoRevisionsAsync(repoRelativePath, olderRev, newerRev);
        }
    }
}
