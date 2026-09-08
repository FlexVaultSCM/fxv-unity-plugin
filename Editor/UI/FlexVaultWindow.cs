using System;
using System.Collections.Generic;
using System.Linq;
using FlexVault.VCS.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.UI
{
    public class FlexVaultWindow : EditorWindow
    {
        private enum Tab
        {
            Changes,
            History
        }

        private enum ChangesViewMode
        {
            WorkspaceChanges,
            UnpublishedDrafts,
            AllChanges
        }

        private Tab m_currentTab = Tab.Changes;
        private ChangesViewMode m_changesViewMode = ChangesViewMode.WorkspaceChanges;
        private Vector2 m_scrollPos;
        private Vector2 m_historyScrollPos;
        private string m_commitDescription = string.Empty;
        private readonly HashSet<string> m_selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<CommitRefJson> m_historyEntries = new List<CommitRefJson>();
        private readonly HashSet<string> m_expandedRevisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ChangeInfoPayload> m_changeInfoCache = new Dictionary<string, ChangeInfoPayload>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> m_loadingChangeInfo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool m_isLoadingHistory;
        private bool m_isOperating;

        [MenuItem("Window/Version Control/FlexVault", false, 10)]
        public static void ShowWindow()
        {
            var window = GetWindow<FlexVaultWindow>("FlexVault");
            window.minSize = new Vector2(400, 350);
            window.Show();
        }

        private void OnEnable()
        {
            FlexVaultStateCache.OnStateChanged += OnStateChanged;
            if (FlexVaultSettings.IsInFlexVaultRepository())
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
            if (FlexVaultSettings.IsInFlexVaultRepository())
            {
                FlexVaultStateCache.RefreshAsync();
            }
        }

        private void OnStateChanged()
        {
            Repaint();
        }

        private void OnGUI()
        {
            if (!FlexVaultSettings.IsInFlexVaultRepository())
            {
                DrawNotRepositoryUI();
                return;
            }

            DrawHeaderToolbar();

            GUILayout.Space(2f);
            DrawTabBar();
            GUILayout.Space(4f);

            if (m_currentTab == Tab.Changes)
            {
                DrawChangesTab();
            }
            else
            {
                DrawHistoryTab();
            }

            DrawBottomStatusBar();
        }

        private void DrawBottomStatusBar()
        {
            var status = FlexVaultStateCache.LatestStatus;
            var syncStatus = status?.SyncStatus;

            string syncedRevText = syncStatus?.SyncedRevision != null
                ? (status.CurrentBranch != null ? $"{status.CurrentBranch}.{syncStatus.SyncedRevision.Value}" : syncStatus.SyncedRevision.Value.ToString())
                : (status?.HeadCommit?.LocalSnapshot != null ? status.HeadCommit.LocalSnapshot.RevisionDisplay : "None");

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                string syncIcon = (syncStatus != null && !syncStatus.UpToDate && syncStatus.RevisionsBehind > 0) ? "▼" : "✓";
                string statusText = (syncStatus != null && !syncStatus.UpToDate && syncStatus.RevisionsBehind > 0)
                    ? $"{syncIcon} Synced: {syncedRevText} ({syncStatus.RevisionsBehind} behind remote HEAD {syncStatus.PublishedHeadRevision})"
                    : $"{syncIcon} Synced: {syncedRevText}";

                GUILayout.Label(statusText, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                if (FlexVaultStateCache.IsRefreshing)
                {
                    GUILayout.Label("Refreshing...", EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawNotRepositoryUI()
        {
            GUILayout.Space(20f);
            EditorGUILayout.HelpBox(
                "No FlexVault repository (.fxv_workspace) detected in this project or its parent directories.",
                MessageType.Warning);

            GUILayout.Space(10f);
            if (GUILayout.Button("Open FlexVault Settings", GUILayout.Height(30)))
            {
                SettingsService.OpenProjectSettings("Project/Version Control/FlexVault");
            }
            GUILayout.Space(5f);
            if (GUILayout.Button("Retry Repository Detection", GUILayout.Height(25)))
            {
                FlexVaultSettings.InvalidateRepoRoot();
                Repaint();
            }
        }

        private void DrawHeaderToolbar()
        {
            var status = FlexVaultStateCache.LatestStatus;
            string branch = status?.CurrentBranch ?? "detecting...";
            string user = !string.IsNullOrEmpty(status?.CurrentUser) ? status.CurrentUser : "Logged out";
            var syncStatus = status?.SyncStatus;

            string syncedRevText = syncStatus?.SyncedRevision != null
                ? (status.CurrentBranch != null ? $"{status.CurrentBranch}.{syncStatus.SyncedRevision.Value}" : syncStatus.SyncedRevision.Value.ToString())
                : (status?.HeadCommit?.LocalSnapshot != null ? status.HeadCommit.LocalSnapshot.RevisionDisplay : "None");

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUILayout.Label($"Branch: {branch}", EditorStyles.boldLabel);
                GUILayout.Label($"Synced: {syncedRevText}", EditorStyles.miniLabel);

                if (GUILayout.Button("Go To...", EditorStyles.toolbarDropDown, GUILayout.Width(70)))
                {
                    PromptGotoRevision();
                }

                string syncLabel = "Sync Latest";
                int syncWidth = 85;
                if (syncStatus != null && !syncStatus.UpToDate && syncStatus.RevisionsBehind > 0)
                {
                    syncLabel = $"Sync Latest ({syncStatus.RevisionsBehind} behind)";
                    syncWidth = 145;
                }

                GUI.enabled = !FlexVaultStateCache.IsRefreshing && !m_isOperating;
                if (GUILayout.Button(syncLabel, EditorStyles.toolbarButton, GUILayout.Width(syncWidth)))
                {
                    SyncWorkspace();
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label($"User: {user}", EditorStyles.miniLabel);

                GUI.enabled = !FlexVaultStateCache.IsRefreshing;
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    FlexVaultStateCache.RefreshAsync(skipScan: false, force: true);
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTabBar()
        {
            int workspaceCount = FlexVaultStateCache.GetWorkspaceChanges().Count;
            string changesTitle = workspaceCount > 0 ? $"Changes ({workspaceCount})" : "Changes";

            string[] tabLabels = new[] { changesTitle, "History" };

            int newTab = GUILayout.Toolbar((int)m_currentTab, tabLabels);
            if (newTab != (int)m_currentTab)
            {
                m_currentTab = (Tab)newTab;
                if (m_currentTab == Tab.History)
                {
                    LoadHistoryEntries();
                }
            }
        }

        private void DrawChangesTab()
        {
            var syncStatus = FlexVaultStateCache.LatestStatus?.SyncStatus;
            if (syncStatus != null && !syncStatus.UpToDate && syncStatus.RevisionsBehind > 0)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                {
                    EditorGUILayout.LabelField(
                        $"Workspace is {syncStatus.RevisionsBehind} revision(s) behind remote HEAD (Rev: {syncStatus.PublishedHeadRevision}).",
                        EditorStyles.wordWrappedLabel);

                    GUI.enabled = !m_isOperating && !FlexVaultStateCache.IsRefreshing;
                    if (GUILayout.Button("Sync Latest", GUILayout.Width(95), GUILayout.Height(22)))
                    {
                        SyncWorkspace();
                    }
                    GUI.enabled = true;
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(4f);
            }

            var workspaceChanges = FlexVaultStateCache.GetWorkspaceChanges();
            var unpublishedChanges = FlexVaultStateCache.GetUnpublishedChanges();
            var allChanges = FlexVaultStateCache.GetChangedFiles();

            // View selector toolbar
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("View:", GUILayout.Width(38));
                string[] viewLabels = new[]
                {
                    $"Pending Changes ({workspaceChanges.Count})",
                    $"Unpublished Draft ({unpublishedChanges.Count})",
                    $"All ({allChanges.Count})"
                };
                m_changesViewMode = (ChangesViewMode)GUILayout.Toolbar((int)m_changesViewMode, viewLabels);
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6f);

            List<FileStatusItem> displayFiles;
            switch (m_changesViewMode)
            {
                case ChangesViewMode.WorkspaceChanges:
                    displayFiles = workspaceChanges;
                    break;
                case ChangesViewMode.UnpublishedDrafts:
                    displayFiles = unpublishedChanges;
                    break;
                default:
                    displayFiles = allChanges;
                    break;
            }

            if (displayFiles.Count == 0)
            {
                GUILayout.Space(15f);
                string msg;
                switch (m_changesViewMode)
                {
                    case ChangesViewMode.WorkspaceChanges:
                        msg = unpublishedChanges.Count > 0
                            ? $"Working tree is clean. All local modifications are saved in snapshots ({unpublishedChanges.Count} draft file(s) awaiting remote publish)."
                            : "Working tree is clean. No pending workspace changes.";
                        break;
                    case ChangesViewMode.UnpublishedDrafts:
                        msg = "All draft revisions have been published to the remote store.";
                        break;
                    default:
                        msg = "Working tree is clean. No pending or unpublished changes.";
                        break;
                }
                EditorGUILayout.HelpBox(msg, MessageType.Info);
            }

            int conflictCount = 0;
            foreach (var f in displayFiles)
            {
                if (f.EffectiveState.Equals("conflicted", StringComparison.OrdinalIgnoreCase)) conflictCount++;
            }

            if (conflictCount > 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    EditorGUILayout.LabelField($"<b>{conflictCount} Conflicted File(s) Detected</b>", EditorStyles.wordWrappedLabel);
                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("Resolve All (Keep Mine)", GUILayout.Height(24)))
                        {
                            ResolveConflicts(FxvRunner.ResolveAction.Mine, null);
                        }
                        if (GUILayout.Button("Resolve All (Take Theirs)", GUILayout.Height(24)))
                        {
                            ResolveConflicts(FxvRunner.ResolveAction.Theirs, null);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
                GUILayout.Space(4f);
            }

            if (displayFiles.Count > 0)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                {
                    if (GUILayout.Button("Select All", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    {
                        foreach (var f in displayFiles)
                        {
                            m_selectedPaths.Add(f.Path);
                        }
                    }
                    if (GUILayout.Button("Select None", EditorStyles.toolbarButton, GUILayout.Width(75)))
                    {
                        m_selectedPaths.Clear();
                    }

                    GUILayout.FlexibleSpace();

                    GUI.enabled = m_selectedPaths.Count == 1 && !m_isOperating;
                    if (GUILayout.Button("Diff Selected", EditorStyles.toolbarButton))
                    {
                        DiffSingleSelectedFile();
                    }
                    GUI.enabled = true;

                    GUI.enabled = m_selectedPaths.Count > 0 && !m_isOperating;
                    if (GUILayout.Button($"Revert Selected ({m_selectedPaths.Count})", EditorStyles.toolbarButton))
                    {
                        RevertSelectedFiles();
                    }
                    GUI.enabled = true;
                }
                EditorGUILayout.EndHorizontal();

                m_scrollPos = EditorGUILayout.BeginScrollView(m_scrollPos, GUILayout.ExpandHeight(true));
                {
                    foreach (var item in displayFiles)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            bool isSelected = m_selectedPaths.Contains(item.Path);
                            bool newSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(20));
                            if (newSelected != isSelected)
                            {
                                if (newSelected) m_selectedPaths.Add(item.Path);
                                else m_selectedPaths.Remove(item.Path);
                            }

                            string displayState = m_changesViewMode == ChangesViewMode.WorkspaceChanges
                                ? item.EffectiveWorkspaceState
                                : item.EffectiveState;

                            DrawStateBadge(displayState);

                            if (GUILayout.Button(item.Path, EditorStyles.linkLabel))
                            {
                                FlexVaultMetaHelper.PingAsset(item.Path);
                            }

                            bool isConflict = item.EffectiveState.Equals("conflicted", StringComparison.OrdinalIgnoreCase);
                            if (isConflict)
                            {
                                if (GUILayout.Button("Mine", EditorStyles.miniButtonLeft, GUILayout.Width(40)))
                                {
                                    ResolveConflicts(FxvRunner.ResolveAction.Mine, new[] { item.Path });
                                }
                                if (GUILayout.Button("Theirs", EditorStyles.miniButtonRight, GUILayout.Width(45)))
                                {
                                    ResolveConflicts(FxvRunner.ResolveAction.Theirs, new[] { item.Path });
                                }
                            }

                            if (GUILayout.Button("Diff", EditorStyles.miniButton, GUILayout.Width(45)))
                            {
                                _ = FlexVaultDiffHelper.DiffFileAgainstBaseAsync(item.Path);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndScrollView();
            }

            GUILayout.Space(5f);
            EditorGUILayout.LabelField("Commit Description:", EditorStyles.boldLabel);
            m_commitDescription = EditorGUILayout.TextArea(m_commitDescription, GUILayout.Height(45));

            GUILayout.Space(3f);
            EditorGUILayout.BeginHorizontal();
            {
                GUI.enabled = !m_isOperating && workspaceChanges.Count > 0;
                if (GUILayout.Button("Create Local Snapshot", GUILayout.Height(32)))
                {
                    CreateLocalSnapshot();
                }

                GUI.enabled = !m_isOperating && (workspaceChanges.Count > 0 || unpublishedChanges.Count > 0) && !string.IsNullOrWhiteSpace(m_commitDescription);
                if (GUILayout.Button("Publish to Remote", GUILayout.Height(32)))
                {
                    PublishChanges();
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2f);
            EditorGUILayout.LabelField("Note: Local snapshots capture all pending workspace changes.", EditorStyles.miniLabel);
            GUILayout.Space(5f);
        }


        private void DrawStateBadge(string state)
        {
            Color color;
            string text;

            switch (state?.ToLowerInvariant())
            {
                case "added":
                    color = new Color(0.2f, 0.7f, 0.3f);
                    text = "[+] Added";
                    break;
                case "modified":
                    color = new Color(0.2f, 0.5f, 0.9f);
                    text = "[~] Modified";
                    break;
                case "deleted":
                    color = new Color(0.9f, 0.2f, 0.2f);
                    text = "[-] Deleted";
                    break;
                case "conflicted":
                    color = new Color(0.95f, 0.4f, 0.1f);
                    text = "[!] Conflicted";
                    break;
                default:
                    color = Color.gray;
                    text = "[?] Changed";
                    break;
            }

            Color prevCol = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(text, EditorStyles.miniBoldLabel, GUILayout.Width(80));
            GUI.contentColor = prevCol;
        }

        private async void CreateLocalSnapshot()
        {
            if (!FlexVaultSafetyGuards.EnsureSafeToMutateWorkspace("Snapshot", promptSaveDirtyScenes: true)) return;

            string desc = string.IsNullOrWhiteSpace(m_commitDescription)
                ? $"Manual draft snapshot at {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                : m_commitDescription.Trim();

            m_isOperating = true;
            EditorApplication.LockReloadAssemblies();

            try
            {
                EditorUtility.DisplayProgressBar("FlexVault", "Creating local draft snapshot...", 0.5f);
                var snapResult = await FxvRunner.SnapshotAsync(desc);
                if (!snapResult.Success)
                {
                    EditorUtility.DisplayDialog("Snapshot Failed", snapResult.ErrorMessage, "OK");
                    return;
                }

                m_commitDescription = string.Empty;
                m_selectedPaths.Clear();
                Debug.Log($"[FlexVault] Local draft snapshot created: {desc}");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Snapshot Error", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EditorApplication.UnlockReloadAssemblies();
                m_isOperating = false;
                FlexVaultStateCache.RefreshAsync();
            }
        }

        private async void PublishChanges()
        {
            if (string.IsNullOrWhiteSpace(m_commitDescription))
            {
                EditorUtility.DisplayDialog("Publish", "Please provide a commit description before publishing.", "OK");
                return;
            }

            if (!FlexVaultSafetyGuards.EnsureSafeToMutateWorkspace("Publish", promptSaveDirtyScenes: true)) return;

            var status = FlexVaultStateCache.LatestStatus;
            if (string.IsNullOrEmpty(status?.CurrentUser))
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "User Identity Warning",
                    "No logged-in FlexVault user was detected.\n'fxv publish' requires an active login.\nDo you want to proceed anyway?",
                    "Proceed",
                    "Cancel");

                if (!proceed)
                {
                    return;
                }
            }

            m_isOperating = true;
            EditorApplication.LockReloadAssemblies();

            try
            {
                EditorUtility.DisplayProgressBar("FlexVault", "Phase 1/2: Creating snapshot...", 0.3f);
                var snapResult = await FxvRunner.SnapshotAsync(m_commitDescription);
                if (!snapResult.Success)
                {
                    EditorUtility.DisplayDialog("Snapshot Failed", snapResult.ErrorMessage, "OK");
                    return;
                }

                EditorUtility.DisplayProgressBar("FlexVault", "Phase 2/2: Publishing draft...", 0.7f);
                var pubResult = await FxvRunner.PublishAsync(m_commitDescription);
                if (!pubResult.Success)
                {
                    EditorUtility.DisplayDialog(
                        "Publish Failed",
                        $"Snapshot succeeded locally, but publish failed:\n\n{pubResult.ErrorMessage}\n\nYour changes remain saved as an unpublished draft.",
                        "OK");
                    return;
                }

                m_commitDescription = string.Empty;
                m_selectedPaths.Clear();
                EditorUtility.DisplayDialog("Publish Succeeded", "Workspace changes were published successfully.", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Publish Error", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorApplication.UnlockReloadAssemblies();
                m_isOperating = false;
                FlexVaultStateCache.RefreshAsync();
            }
        }

        private async void RevertSelectedFiles()
        {
            if (m_selectedPaths.Count == 0)
            {
                return;
            }

            if (!FlexVaultSafetyGuards.EnsureSafeToMutateWorkspace("Revert", promptSaveDirtyScenes: true)) return;

            var repoRelative = FlexVaultMetaHelper.ExpandWithMeta(m_selectedPaths);

            if (!EditorUtility.DisplayDialog(
                "Confirm Revert",
                $"Revert {m_selectedPaths.Count} selected file(s) and their companion .meta files to published base?\nAll working tree changes to these files will be lost.",
                "Revert",
                "Cancel"))
            {
                return;
            }

            m_isOperating = true;
            EditorApplication.LockReloadAssemblies();

            try
            {
                EditorUtility.DisplayProgressBar("FlexVault", "Reverting files...", 0.5f);
                var result = await FxvRunner.RevertAsync(repoRelative);
                if (!result.Success)
                {
                    EditorUtility.DisplayDialog("Revert Failed", result.ErrorMessage, "OK");
                }
                else
                {
                    m_selectedPaths.Clear();
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Revert Error", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorApplication.UnlockReloadAssemblies();
                m_isOperating = false;
                FlexVaultStateCache.RefreshAsync();
            }
        }

        private async void SyncWorkspace(string targetRevision = null)
        {
            if (!FlexVaultSafetyGuards.EnsureSafeToMutateWorkspace("Sync", promptSaveDirtyScenes: true)) return;

            m_isOperating = true;
            EditorApplication.LockReloadAssemblies();

            try
            {
                EditorUtility.DisplayProgressBar("FlexVault", "Syncing workspace with remote repository...", 0.5f);
                string rev = string.IsNullOrWhiteSpace(targetRevision) ? null : targetRevision.Trim();
                var result = await FxvRunner.SyncAsync(rev);

                if (!result.Success)
                {
                    EditorUtility.DisplayDialog("Sync Failed", result.ErrorMessage, "OK");
                }
                else
                {
                    if (result.Data?.ConflictedFiles != null && result.Data.ConflictedFiles.Count > 0)
                    {
                        string conflictList = string.Join("\n", result.Data.ConflictedFiles);
                        EditorUtility.DisplayDialog(
                            "Sync Conflicts Detected",
                            $"Sync completed with {result.Data.ConflictedFiles.Count} conflict(s):\n\n{conflictList}\n\nPlease resolve conflicts with 'fxv resolve' before publishing.",
                            "OK");
                    }
                    else
                    {
                        string msg = $"Workspace updated to {result.Data?.TargetRevision ?? "HEAD"} ({result.Data?.FilesUpdatedCount ?? 0} file(s) updated).";
                        ShowNotification(new GUIContent(msg));
                        Debug.Log($"[FlexVault] {msg}");
                    }
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Sync Error", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorApplication.UnlockReloadAssemblies();
                m_isOperating = false;
                FlexVaultStateCache.RefreshAsync();
            }
        }

        private async void ResolveConflicts(FxvRunner.ResolveAction action, IEnumerable<string> paths)
        {
            if (!FlexVaultSafetyGuards.EnsureSafeToMutateWorkspace("Resolve Conflicts", promptSaveDirtyScenes: true)) return;

            string actionName = action == FxvRunner.ResolveAction.Mine ? "Keep Mine" : "Take Theirs";
            string targetDesc = paths != null ? $"{paths.Count()} file(s)" : "all conflicted files";

            if (!EditorUtility.DisplayDialog(
                "Resolve Conflicts",
                $"Resolve {targetDesc} with '{actionName}'?\nThis will update your draft to reflect the resolution.",
                "Resolve",
                "Cancel"))
            {
                return;
            }

            m_isOperating = true;
            EditorApplication.LockReloadAssemblies();

            try
            {
                EditorUtility.DisplayProgressBar("FlexVault", $"Resolving conflicts ({actionName})...", 0.5f);
                var result = await FxvRunner.ResolveAsync(action, paths);
                if (!result.Success)
                {
                    EditorUtility.DisplayDialog("Resolve Failed", result.ErrorMessage, "OK");
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Resolve Error", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorApplication.UnlockReloadAssemblies();
                m_isOperating = false;
                FlexVaultStateCache.RefreshAsync();
            }
        }

        private void DiffSingleSelectedFile()
        {
            if (m_selectedPaths.Count != 1) return;
            foreach (var p in m_selectedPaths)
            {
                _ = FlexVaultDiffHelper.DiffFileAgainstBaseAsync(p);
                break;
            }
        }

        private async void LoadHistoryEntries()
        {
            if (!FlexVaultSettings.IsInFlexVaultRepository()) return;
            m_isLoadingHistory = true;
            Repaint();

            try
            {
                var result = await FxvRunner.GetHistoryAsync(count: 50);
                if (result.Success && result.Data != null)
                {
                    m_historyEntries = result.Data.Entries ?? new List<CommitRefJson>();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FlexVault] Failed to load history: {ex.Message}");
            }
            finally
            {
                m_isLoadingHistory = false;
                Repaint();
            }
        }

        private void DrawHistoryTab()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUILayout.Label("Recent Commits", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                GUI.enabled = !m_isLoadingHistory;
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(65)))
                {
                    LoadHistoryEntries();
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();

            if (m_isLoadingHistory)
            {
                EditorGUILayout.HelpBox("Loading revision history...", MessageType.Info);
                return;
            }

            if (m_historyEntries.Count == 0)
            {
                GUILayout.Space(20f);
                EditorGUILayout.HelpBox("No revision history found for this branch.", MessageType.Info);
                return;
            }

            m_historyScrollPos = EditorGUILayout.BeginScrollView(m_historyScrollPos, GUILayout.ExpandHeight(true));
            {
                for (int i = 0; i < m_historyEntries.Count; i++)
                {
                    var entry = m_historyEntries[i];
                    bool isCurrent = FlexVaultStateCache.IsCurrentWorkspaceRevision(entry);

                    var prevBg = GUI.backgroundColor;
                    if (isCurrent)
                    {
                        GUI.backgroundColor = EditorGUIUtility.isProSkin
                            ? new Color(0.18f, 0.42f, 0.28f, 1f)
                            : new Color(0.72f, 0.92f, 0.78f, 1f);
                    }
                    var bg = (i % 2 == 0) ? EditorStyles.helpBox : EditorStyles.textArea;
                    EditorGUILayout.BeginVertical(bg);
                    GUI.backgroundColor = prevBg;

                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            bool isExpanded = m_expandedRevisions.Contains(entry.RevisionDisplay);
                            string arrow = isExpanded ? "\u25BC" : "\u25B6";
                            if (GUILayout.Button(arrow, EditorStyles.miniButton, GUILayout.Width(24)))
                            {
                                if (isExpanded)
                                {
                                    m_expandedRevisions.Remove(entry.RevisionDisplay);
                                }
                                else
                                {
                                    m_expandedRevisions.Add(entry.RevisionDisplay);
                                    EnsureChangeInfoLoaded(entry.RevisionDisplay);
                                }
                            }

                            if (isCurrent)
                            {
                                Color prevCol2 = GUI.contentColor;
                                GUI.contentColor = new Color(0.2f, 0.9f, 0.4f);
                                GUILayout.Label("● Current", EditorStyles.boldLabel, GUILayout.Width(68));
                                GUI.contentColor = prevCol2;
                            }

                            bool isPublished = entry.Commit?.Type == "published";
                            Color col = isPublished ? new Color(0.2f, 0.6f, 1f) : new Color(0.85f, 0.5f, 0.1f);
                            string tag = isPublished ? "[Published]" : "[Draft]";

                            Color prevCol = GUI.contentColor;
                            GUI.contentColor = col;
                            GUILayout.Label(tag, EditorStyles.miniBoldLabel, GUILayout.Width(75));
                            GUI.contentColor = prevCol;

                            GUILayout.Label(entry.RevisionDisplay, EditorStyles.boldLabel, GUILayout.Width(110));
                            GUILayout.Label(entry.AuthorDisplayName ?? entry.AuthorId ?? "Unknown", EditorStyles.miniLabel, GUILayout.Width(100));

                            string date = entry.TimestampMillisSinceEpochUtc > 0
                                ? entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                                : string.Empty;
                            GUILayout.Label(date, EditorStyles.miniLabel, GUILayout.Width(110));

                            GUILayout.FlexibleSpace();
                            GUI.enabled = !isCurrent && !m_isOperating;
                            if (GUILayout.Button(isCurrent ? "Current" : "Go To", EditorStyles.miniButton, GUILayout.Width(65)))
                            {
                                ExecuteGoto(entry.RevisionDisplay);
                            }
                            GUI.enabled = true;
                        }
                        EditorGUILayout.EndHorizontal();

                        string desc = !string.IsNullOrWhiteSpace(entry.Description) ? entry.Description.Trim() : "(No description)";
                        EditorGUILayout.LabelField(desc, EditorStyles.wordWrappedLabel);

                        if (m_expandedRevisions.Contains(entry.RevisionDisplay))
                        {
                            DrawExpandedChanges(entry, i);
                        }
                    }
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(2f);
                }
            }
            EditorGUILayout.EndScrollView();
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
                        DrawHistoryActionBadge(file.Action);

                        if (GUILayout.Button(file.Path, EditorStyles.linkLabel))
                        {
                            FlexVaultMetaHelper.PingAsset(file.Path);
                        }

                        GUILayout.FlexibleSpace();

                        if (!string.Equals(file.Action, "deleted", StringComparison.OrdinalIgnoreCase))
                        {
                            if (GUILayout.Button("Diff vs Current", EditorStyles.miniButton, GUILayout.Width(90)))
                            {
                                _ = FlexVaultDiffHelper.DiffWithWorkingCopyAsync(file.Path, rev);
                            }

                            if (commitIndex + 1 < m_historyEntries.Count)
                            {
                                var prev = m_historyEntries[commitIndex + 1];
                                if (GUILayout.Button("Diff vs Prev", EditorStyles.miniButton, GUILayout.Width(80)))
                                {
                                    _ = FlexVaultDiffHelper.DiffTwoRevisionsAsync(file.Path, prev.RevisionDisplay, rev);
                                }
                            }
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawHistoryActionBadge(string action)
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

        private void PromptGotoRevision()
        {
            EditorInputDialog.Show("Go To Revision", "Enter target revision spec to move workspace state to (e.g. main.11 or main.11.2):", "", (rev) =>
            {
                if (!string.IsNullOrWhiteSpace(rev))
                {
                    ExecuteGoto(rev.Trim());
                }
            });
        }

        public static async void ExecuteGotoRevision(string targetRevision, Action onComplete = null)
        {
            if (!FlexVaultSafetyGuards.EnsureSafeToMutateWorkspace("Go To Revision", promptSaveDirtyScenes: true))
            {
                onComplete?.Invoke();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                "Confirm Go To Revision",
                $"Move workspace to '{targetRevision}'?\nYour current workspace will be snapshotted first to preserve local work.",
                "Go To",
                "Cancel"))
            {
                onComplete?.Invoke();
                return;
            }

            EditorApplication.LockReloadAssemblies();

            try
            {
                EditorUtility.DisplayProgressBar("FlexVault", $"Moving workspace to {targetRevision}...", 0.5f);
                var result = await FxvRunner.GotoAsync(targetRevision);
                if (!result.Success)
                {
                    EditorUtility.DisplayDialog("Go To Failed", result.ErrorMessage, "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Go To Complete", $"Workspace state moved to '{targetRevision}'.", "OK");
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Go To Error", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorApplication.UnlockReloadAssemblies();
                FlexVaultStateCache.RefreshAsync();
                onComplete?.Invoke();
            }
        }

        private void ExecuteGoto(string targetRevision)
        {
            m_isOperating = true;
            ExecuteGotoRevision(targetRevision, () =>
            {
                m_isOperating = false;
                Repaint();
            });
        }
    }

    public class EditorInputDialog : EditorWindow
    {
        private string m_prompt;
        private string m_inputText;
        private Action<string> m_onConfirm;

        public static void Show(string title, string prompt, string defaultText, Action<string> onConfirm)
        {
            var window = CreateInstance<EditorInputDialog>();
            window.titleContent = new GUIContent(title);
            window.m_prompt = prompt;
            window.m_inputText = defaultText ?? "";
            window.minSize = new Vector2(380, 130);
            window.maxSize = new Vector2(380, 130);
            window.m_onConfirm = onConfirm;
            window.ShowUtility();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField(m_prompt ?? "", EditorStyles.wordWrappedLabel);
            GUILayout.Space(5);
            m_inputText = EditorGUILayout.TextField(m_inputText);
            GUILayout.Space(15);

            bool submit = false;
            Event e = Event.current;
            if (e != null && e.isKey && e.keyCode == KeyCode.Return && e.type == EventType.KeyDown)
            {
                submit = true;
                e.Use();
            }

            bool doClose = false;
            Action actionToInvoke = null;

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(80)))
                {
                    doClose = true;
                }
                if (GUILayout.Button("OK", GUILayout.Width(80)) || submit)
                {
                    string text = m_inputText;
                    var callback = m_onConfirm;
                    doClose = true;
                    actionToInvoke = () => callback?.Invoke(text);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (doClose)
            {
                Close();
                if (actionToInvoke != null)
                {
                    EditorApplication.delayCall += () => actionToInvoke();
                }
                GUIUtility.ExitGUI();
            }
        }
    }
}
