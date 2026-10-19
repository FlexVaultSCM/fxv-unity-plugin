using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlexVault.VCS.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.UI
{
    public class FlexVaultHistoryWindow : EditorWindow
    {
        private enum HistoryFilter
        {
            All,
            Drafts,
            Published
        }

        private Vector2 m_scrollPos;
        private List<CommitRefJson> m_allEntries = new List<CommitRefJson>();
        private List<CommitRefJson> m_entries = new List<CommitRefJson>();
        private bool m_isLoading;
        private string m_filterPath = string.Empty;
        private string m_statusMessage = string.Empty;
        private HistoryFilter m_historyFilter = HistoryFilter.All;
        private string m_lastHistoryFingerprint = string.Empty;
        private CancellationTokenSource m_loadHistoryCts;

        private const float HistoryColToggleWidth = 24f;
        private const float HistoryColTypeWidth = 85f;
        private const float HistoryColRevisionWidth = 150f;
        private const float HistoryColAuthorWidth = 110f;
        private const float HistoryColDateWidth = 125f;
        private const float HistoryColActionWidth = 65f;

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
            m_loadHistoryCts?.Cancel();
            m_loadHistoryCts?.Dispose();
            m_loadHistoryCts = null;
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
            string fp = FlexVaultStateCache.GetCurrentHistoryFingerprint();
            if (!string.IsNullOrEmpty(fp) && !string.Equals(fp, m_lastHistoryFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                if (!m_isLoading)
                {
                    LoadHistory();
                    return;
                }
            }

            Repaint();
        }

        public void RefreshHistory()
        {
            m_loadHistoryCts?.Cancel();
            m_expandedRevisions.Clear();
            m_changeInfoCache.Clear();
            m_loadingChangeInfo.Clear();
            m_lastHistoryFingerprint = string.Empty;
            LoadHistory();
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

            m_loadHistoryCts?.Cancel();
            m_loadHistoryCts?.Dispose();
            var cts = new CancellationTokenSource();
            m_loadHistoryCts = cts;
            CancellationToken ct = cts.Token;

            string requestFingerprint = FlexVaultStateCache.GetCurrentHistoryFingerprint();
            m_isLoading = true;
            m_statusMessage = "Loading revision history...";
            Repaint();

            try
            {
                int fetchCount = !string.IsNullOrEmpty(m_filterPath) ? 100 : 50;
                var result = await FxvRunner.GetHistoryAsync(count: fetchCount, ct: ct);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (result.Success && result.Data != null)
                {
                    var allEntries = result.Data.Entries ?? new List<CommitRefJson>();
                    m_allEntries = allEntries;

                    if (!string.IsNullOrEmpty(m_filterPath))
                    {
                        m_statusMessage = $"Filtering revisions for '{m_filterPath}'...";
                        Repaint();

                        string normalizedFilter = FlexVaultMetaHelper.NormalizeSeparators(m_filterPath);
                        string companionFilter = FlexVaultMetaHelper.GetCompanionMetaPath(normalizedFilter);
                        var filtered = new List<CommitRefJson>();

                        foreach (var entry in allEntries)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                return;
                            }

                            string rev = entry.RevisionDisplay;
                            string revSpec = entry.RevisionSpec;
                            ChangeInfoPayload payload = null;
                            if (m_changeInfoCache.TryGetValue(rev, out var cached) &&
                                (string.IsNullOrEmpty(entry.CommitHash) || string.IsNullOrEmpty(cached.CommitHash) ||
                                 string.Equals(entry.CommitHash, cached.CommitHash, StringComparison.OrdinalIgnoreCase)))
                            {
                                payload = cached;
                            }
                            else
                            {
                                var changeResult = await FxvRunner.GetChangeInfoAsync(revSpec, ct);
                                if (ct.IsCancellationRequested)
                                {
                                    return;
                                }
                                if (changeResult.Success && changeResult.Data != null)
                                {
                                    payload = changeResult.Data;
                                    m_changeInfoCache[rev] = payload;
                                }
                            }

                            bool touchesTarget = false;
                            if (payload?.Changes != null)
                            {
                                foreach (var change in payload.Changes)
                                {
                                    string changePath = FlexVaultMetaHelper.NormalizeSeparators(change.Path);
                                    if (string.Equals(changePath, normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(changePath, companionFilter, StringComparison.OrdinalIgnoreCase))
                                    {
                                        touchesTarget = true;
                                        break;
                                    }
                                }
                            }

                            if (touchesTarget || FlexVaultStateCache.IsCurrentWorkspaceRevision(entry, allEntries))
                            {
                                filtered.Add(entry);
                            }
                        }

                        m_entries = filtered;
                        m_statusMessage = m_entries.Count == 0 ? $"No revisions found that modified '{m_filterPath}'." : string.Empty;
                    }
                    else
                    {
                        m_entries = allEntries;
                        m_statusMessage = m_entries.Count == 0 ? "No history entries found." : string.Empty;
                    }
                    m_lastHistoryFingerprint = FlexVaultStateCache.GetCurrentHistoryFingerprint();
                }
                else
                {
                    m_statusMessage = $"Failed to load history: {result.ErrorMessage}";
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                m_statusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                if (m_loadHistoryCts == cts)
                {
                    m_isLoading = false;
                    string currentFingerprint = FlexVaultStateCache.GetCurrentHistoryFingerprint();
                    if (!string.IsNullOrEmpty(currentFingerprint) &&
                        !string.Equals(currentFingerprint, m_lastHistoryFingerprint, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(currentFingerprint, requestFingerprint, StringComparison.OrdinalIgnoreCase))
                    {
                        LoadHistory();
                    }
                    else
                    {
                        Repaint();
                    }
                }
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

                string[] filterLabels = { "All", "Drafts", "Published" };
                m_historyFilter = (HistoryFilter)GUILayout.Toolbar((int)m_historyFilter, filterLabels, EditorStyles.toolbarButton, GUILayout.Width(180));

                if (GUILayout.Button("New Branch...", EditorStyles.toolbarButton, GUILayout.Width(85)))
                {
                    FlexVaultCreateBranchDialog.ShowWindow();
                }

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

            if (m_historyFilter != HistoryFilter.All && m_entries.Count > 0 && !m_entries.Any(MatchesHistoryFilter))
            {
                EditorGUILayout.HelpBox($"No {m_historyFilter.ToString().ToLowerInvariant()} found in the loaded history.", MessageType.Info);
            }

            if (m_entries.Count > 0)
            {
                float helpBoxLeft = EditorStyles.helpBox.margin.left + EditorStyles.helpBox.padding.left;
                float helpBoxRight = EditorStyles.helpBox.margin.right + EditorStyles.helpBox.padding.right;

                EditorGUILayout.BeginHorizontal();
                {
                    GUILayout.Space(helpBoxLeft + HistoryColToggleWidth + 4f);
                    GUILayout.Label("Type", EditorStyles.miniBoldLabel, GUILayout.Width(HistoryColTypeWidth));
                    GUILayout.Label("Revision", EditorStyles.miniBoldLabel, GUILayout.Width(HistoryColRevisionWidth));
                    GUILayout.Label("Author", EditorStyles.miniBoldLabel, GUILayout.Width(HistoryColAuthorWidth));
                    GUILayout.Label("Date", EditorStyles.miniBoldLabel, GUILayout.Width(HistoryColDateWidth));
                    GUILayout.FlexibleSpace();
                    GUILayout.Space(HistoryColActionWidth + helpBoxRight + 4f);
                }
                EditorGUILayout.EndHorizontal();
            }

            m_scrollPos = EditorGUILayout.BeginScrollView(m_scrollPos, GUILayout.ExpandHeight(true));
            {
                int visibleIndex = 0;
                for (int i = 0; i < m_entries.Count; i++)
                {
                    var entry = m_entries[i];
                    if (!MatchesHistoryFilter(entry)) continue;
                    DrawHistoryEntry(entry, i, visibleIndex++);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool MatchesHistoryFilter(CommitRefJson entry)
        {
            if (m_historyFilter == HistoryFilter.All) return true;

            bool isDraft = string.Equals(entry.Commit?.Type, "draft", StringComparison.OrdinalIgnoreCase);
            return m_historyFilter == HistoryFilter.Drafts ? isDraft : !isDraft;
        }

        private readonly HashSet<string> m_expandedRevisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ChangeInfoPayload> m_changeInfoCache = new Dictionary<string, ChangeInfoPayload>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> m_loadingChangeInfo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void DrawHistoryEntry(CommitRefJson entry, int index, int visibleIndex)
        {
            bool isCurrent = FlexVaultStateCache.IsCurrentWorkspaceRevision(entry, m_entries);
            var prevBg = GUI.backgroundColor;
            if (isCurrent)
            {
                GUI.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.20f, 0.45f, 0.28f, 1f)
                    : new Color(0.72f, 0.92f, 0.78f, 1f);
            }
            else if (visibleIndex % 2 != 0)
            {
                // Zebra-stripe via a background tint rather than swapping GUIStyles: helpBox and
                // textArea have different internal padding, which was shifting every column's
                // content left/right depending on which style a given row landed on.
                GUI.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.85f, 0.85f, 0.85f, 1f)
                    : new Color(0.93f, 0.93f, 0.93f, 1f);
            }
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            {
                EditorGUILayout.BeginHorizontal();
                {
                    string rev = entry.RevisionDisplay;
                    string revSpec = entry.RevisionSpec;
                    bool isExpanded = m_expandedRevisions.Contains(rev);
                    string toggleSymbol = isExpanded ? "\u25BC" : "\u25B6";
                    if (GUILayout.Button(toggleSymbol, EditorStyles.miniButton, GUILayout.Width(HistoryColToggleWidth)))
                    {
                        if (isExpanded)
                        {
                            m_expandedRevisions.Remove(rev);
                        }
                        else
                        {
                            m_expandedRevisions.Add(rev);
                            EnsureChangeInfoLoaded(rev, revSpec);
                        }
                    }

                    bool isPublished = entry.Commit?.Type == "published";
                    Color badgeColor = isPublished ? new Color(0.2f, 0.6f, 1f) : new Color(0.85f, 0.5f, 0.1f);
                    string typeLabel = isPublished ? "[Published]" : "[Draft]";

                    Color prevCol = GUI.contentColor;
                    GUI.contentColor = badgeColor;
                    GUILayout.Label(typeLabel, EditorStyles.miniBoldLabel, GUILayout.Width(HistoryColTypeWidth));
                    GUI.contentColor = prevCol;

                    GUILayout.Label(rev, EditorStyles.boldLabel, GUILayout.Width(HistoryColRevisionWidth));

                    string author = !string.IsNullOrEmpty(entry.AuthorDisplayName)
                        ? entry.AuthorDisplayName
                        : (!string.IsNullOrEmpty(entry.AuthorId) ? entry.AuthorId : "Unknown");
                    GUILayout.Label(author, EditorStyles.miniLabel, GUILayout.Width(HistoryColAuthorWidth));

                    string timeStr = entry.TimestampMillisSinceEpochUtc > 0
                        ? entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        : string.Empty;
                    GUILayout.Label(timeStr, EditorStyles.miniLabel, GUILayout.Width(HistoryColDateWidth));

                    GUILayout.FlexibleSpace();

                    if (!string.IsNullOrEmpty(m_filterPath))
                    {
                        if (!isCurrent && GUILayout.Button("Diff vs Current", EditorStyles.miniButton, GUILayout.Width(95)))
                        {
                            DiffWithWorkingCopy(m_filterPath, revSpec);
                        }

                        string prevRevSpec = ResolvePredecessorSpecForEntry(entry, index);
                        if (!string.IsNullOrEmpty(prevRevSpec) && !IsTargetFileAddedInEntry(rev, m_filterPath))
                        {
                            if (GUILayout.Button("Diff vs Prev", EditorStyles.miniButton, GUILayout.Width(85)))
                            {
                                DiffTwoRevisions(m_filterPath, prevRevSpec, revSpec);
                            }
                        }
                    }
                    else
                    {
                        GUI.enabled = !isCurrent;
                        if (GUILayout.Button(isCurrent ? "Current" : "Go To", EditorStyles.miniButton, GUILayout.Width(HistoryColActionWidth)))
                        {
                            FlexVaultWindow.ExecuteGotoRevision(revSpec, Repaint);
                        }
                        GUI.enabled = true;
                    }
                }
                EditorGUILayout.EndHorizontal();

                string desc = !string.IsNullOrWhiteSpace(entry.Description) ? entry.Description.Trim() : "(No description)";
                EditorGUILayout.LabelField(desc, EditorStyles.wordWrappedLabel);

                if (m_expandedRevisions.Contains(entry.RevisionDisplay))
                {
                    DrawExpandedChanges(entry, index, isCurrent);
                }
            }
            EditorGUILayout.EndVertical();

            Rect rowRect = GUILayoutUtility.GetLastRect();
            Event evt = Event.current;
            if (evt != null && evt.type == EventType.ContextClick && rowRect.Contains(evt.mousePosition))
            {
                var contextMenu = new GenericMenu();
                string targetRevDisplay = entry.RevisionDisplay;
                string targetRevSpec = entry.RevisionSpec;
                contextMenu.AddItem(new GUIContent($"Create Branch From {targetRevDisplay}..."), false, () => FlexVaultCreateBranchDialog.ShowWindow(targetRevSpec));
                if (!isCurrent)
                {
                    contextMenu.AddItem(new GUIContent($"Go To Revision {targetRevDisplay}"), false, () => FlexVaultWindow.ExecuteGotoRevision(targetRevSpec, Repaint));
                }
                contextMenu.AddItem(new GUIContent("Copy Revision Spec"), false, () => EditorGUIUtility.systemCopyBuffer = targetRevSpec);
                contextMenu.ShowAsContext();
                evt.Use();
            }

            GUI.backgroundColor = prevBg;
            GUILayout.Space(2f);
        }

        private string ResolvePredecessorSpecForEntry(CommitRefJson entry, int filteredIndex)
        {
            var sourceList = m_allEntries != null && m_allEntries.Count > 0 ? m_allEntries : m_entries;
            int sourceIndex = sourceList.IndexOf(entry);
            if (sourceIndex < 0)
            {
                sourceIndex = filteredIndex;
                sourceList = m_entries;
            }
            return CommitRefJson.GetPredecessorRevisionSpec(sourceList, sourceIndex);
        }

        private bool IsTargetFileAddedInEntry(string cacheKeyRevision, string filterPath)
        {
            if (string.IsNullOrEmpty(filterPath) || !m_changeInfoCache.TryGetValue(cacheKeyRevision, out var payload) || payload?.Changes == null)
            {
                return false;
            }

            string normalizedFilter = FlexVaultMetaHelper.NormalizeSeparators(filterPath);
            foreach (var change in payload.Changes)
            {
                string changePath = FlexVaultMetaHelper.NormalizeSeparators(change.Path);
                if (string.Equals(changePath, normalizedFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Equals(change.Action, "added", StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        private async void EnsureChangeInfoLoaded(string cacheKeyRevision, string queryRevisionSpec = null)
        {
            if (m_changeInfoCache.ContainsKey(cacheKeyRevision) || m_loadingChangeInfo.Contains(cacheKeyRevision))
            {
                return;
            }

            string cliRev = !string.IsNullOrEmpty(queryRevisionSpec) ? queryRevisionSpec : cacheKeyRevision;
            m_loadingChangeInfo.Add(cacheKeyRevision);
            try
            {
                var result = await FxvRunner.GetChangeInfoAsync(cliRev);
                if (result.Success && result.Data != null)
                {
                    m_changeInfoCache[cacheKeyRevision] = result.Data;
                }
                else if (!result.Success)
                {
                    Debug.LogWarning($"[FlexVault] Failed to load change details for {cliRev}: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FlexVault] Failed to load change details for {cliRev}: {ex.Message}");
            }
            finally
            {
                m_loadingChangeInfo.Remove(cacheKeyRevision);
                Repaint();
            }
        }

        private void DrawExpandedChanges(CommitRefJson entry, int commitIndex, bool isCurrent)
        {
            string rev = entry.RevisionDisplay;
            string revSpec = entry.RevisionSpec;
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

            string prevRevSpec = ResolvePredecessorSpecForEntry(entry, commitIndex);

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
                            if (!isCurrent && GUILayout.Button("Diff vs Current", EditorStyles.miniButton, GUILayout.Width(90)))
                            {
                                DiffWithWorkingCopy(file.Path, revSpec);
                            }

                            if (!string.IsNullOrEmpty(prevRevSpec) && !string.Equals(file.Action, "added", StringComparison.OrdinalIgnoreCase))
                            {
                                if (GUILayout.Button("Diff vs Prev", EditorStyles.miniButton, GUILayout.Width(80)))
                                {
                                    DiffTwoRevisions(file.Path, prevRevSpec, revSpec);
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
