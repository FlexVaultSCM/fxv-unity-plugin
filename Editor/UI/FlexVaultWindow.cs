using System;
using System.Collections.Generic;
using System.Linq;
using FlexVault.VCS.Editor.Core;
using FlexVault.VCS.Editor.Hooks;
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

        // A large enough paste makes EditorGUILayout.TextArea's word-wrap layout pass slow enough
        // per-frame to freeze the editor; cap input length well below that.
        private const int MaxCommitDescriptionLength = 2000;

        private Tab m_currentTab = Tab.Changes;
        private Vector2 m_scrollPos;
        private Vector2 m_historyScrollPos;
        private string m_commitDescription = string.Empty;
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
                if (m_currentTab == Tab.History)
                {
                    LoadHistoryEntries();
                }
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

            if (FlexVaultVersionGuard.IsVersionCompatible == false)
            {
                DrawVersionMismatchBanner();
            }

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
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
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
                SettingsService.OpenProjectSettings("Project/Version Control");
            }
            GUILayout.Space(5f);
            if (GUILayout.Button("Retry Repository Detection", GUILayout.Height(25)))
            {
                FlexVaultSettings.InvalidateRepoRoot();
                Repaint();
            }
        }

        private void DrawVersionMismatchBanner()
        {
            GUILayout.Space(4f);
            string message = FlexVaultVersionGuard.LastErrorMessage ?? "Incompatible FlexVault CLI version detected.";
            EditorGUILayout.HelpBox(message, MessageType.Error);

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("Open FlexVault Settings...", EditorStyles.miniButton, GUILayout.Width(180)))
                {
                    SettingsService.OpenProjectSettings("Project/Version Control");
                }

                if (GUILayout.Button("Retry Version Check", EditorStyles.miniButton, GUILayout.Width(150)))
                {
                    FlexVaultVersionGuard.ResetCachedVersion();
                    FlexVaultStateCache.RefreshAsync(skipScan: false, force: true);
                }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        private void DrawHeaderToolbar()
        {
            var status = FlexVaultStateCache.LatestStatus;
            string branch;
            if (status != null && !string.IsNullOrEmpty(status.CurrentBranch))
            {
                branch = status.CurrentBranch;
            }
            else if (FlexVaultVersionGuard.IsVersionCompatible == false)
            {
                branch = "incompatible CLI";
            }
            else if (FlexVaultStateCache.IsRefreshing)
            {
                branch = "detecting...";
            }
            else
            {
                branch = "disconnected";
            }
            string user = !string.IsNullOrEmpty(status?.CurrentUser) ? status.CurrentUser : "Logged out";
            var syncStatus = status?.SyncStatus;

            string syncedRevText = syncStatus?.SyncedRevision != null
                ? (status.CurrentBranch != null ? $"{status.CurrentBranch}.{syncStatus.SyncedRevision.Value}" : syncStatus.SyncedRevision.Value.ToString())
                : (status?.HeadCommit?.LocalSnapshot != null ? status.HeadCommit.LocalSnapshot.RevisionDisplay : "None");

            bool isBehind = syncStatus != null && !syncStatus.UpToDate && syncStatus.RevisionsBehind > 0;
            string syncIcon = isBehind ? "▼" : "✓";
            string syncedText = isBehind
                ? $"{syncIcon} Synced: {syncedRevText} ({syncStatus.RevisionsBehind} behind)"
                : $"{syncIcon} Synced: {syncedRevText}";

            var branchStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            var syncedStyle = new GUIStyle(EditorStyles.label) { fontSize = 12 };
            var userStyle = new GUIStyle(EditorStyles.label) { fontSize = 12 };

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(24));
            {
                GUILayout.Label($"Branch: {branch}", branchStyle, GUILayout.ExpandWidth(false));
                GUILayout.Space(14f);
                GUILayout.Label(syncedText, syncedStyle, GUILayout.ExpandWidth(false));
                GUILayout.Space(10f);

                if (GUILayout.Button("Go To...", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    PromptGotoRevision();
                }

                GUI.enabled = !FlexVaultStateCache.IsRefreshing && !m_isOperating;
                if (GUILayout.Button("Sync Latest", EditorStyles.toolbarButton, GUILayout.Width(85)))
                {
                    SyncWorkspace();
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label($"User: {user}", userStyle);

                if (string.IsNullOrEmpty(status?.CurrentUser))
                {
                    GUILayout.Space(4f);
                    if (GUILayout.Button("Log In...", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    {
                        PromptLogin();
                    }
                }

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
            int changedCount = FlexVaultStateCache.GetChangedFiles().Count;
            string changesTitle = changedCount > 0 ? $"Changes ({changedCount})" : "Changes";

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

            var displayFiles = FlexVaultStateCache.GetChangedFiles();

            if (displayFiles.Count == 0)
            {
                GUILayout.Space(15f);
                EditorGUILayout.HelpBox("Working tree is clean. No pending or unpublished changes.", MessageType.Info);
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
                    EditorGUILayout.LabelField($"{conflictCount} Conflicted File(s) Detected", EditorStyles.boldLabel);
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
                m_scrollPos = EditorGUILayout.BeginScrollView(m_scrollPos, GUILayout.ExpandHeight(true));
                {
                    foreach (var item in displayFiles)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            DrawStateBadge(item.EffectiveState);

                            var pathContent = item.ConflictState != null
                                ? new GUIContent(item.Path, item.ConflictState.Description)
                                : new GUIContent(item.Path);
                            if (GUILayout.Button(pathContent, EditorStyles.linkLabel, GUILayout.ExpandWidth(true)))
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

                            GUI.enabled = !m_isOperating;
                            if (GUILayout.Button("Revert", EditorStyles.miniButton, GUILayout.Width(45)))
                            {
                                RevertFile(item.Path);
                            }
                            GUI.enabled = true;

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

            bool isBehindRemote = syncStatus != null && !syncStatus.UpToDate && syncStatus.RevisionsBehind > 0;

            if (isBehindRemote)
            {
                GUILayout.Space(4f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        string behindWarning = $"Workspace is {syncStatus.RevisionsBehind} revision(s) behind remote. Sync required before publishing.";
                        EditorGUILayout.LabelField(behindWarning, EditorStyles.wordWrappedMiniLabel);

                        GUI.enabled = !FlexVaultStateCache.IsRefreshing && !m_isOperating;
                        if (GUILayout.Button("Sync Now", EditorStyles.miniButton, GUILayout.Width(75)))
                        {
                            SyncWorkspace();
                        }
                        GUI.enabled = true;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }

            GUILayout.Space(5f);
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("Commit Description:", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{m_commitDescription.Length}/{MaxCommitDescriptionLength}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            string newDescription = EditorGUILayout.TextArea(m_commitDescription, GUILayout.Height(45));
            if (newDescription.Length > MaxCommitDescriptionLength)
            {
                newDescription = newDescription.Substring(0, MaxCommitDescriptionLength);
            }
            m_commitDescription = newDescription;

            GUILayout.Space(3f);
            EditorGUILayout.BeginHorizontal();
            {
                GUI.enabled = !m_isOperating && displayFiles.Count > 0 && !string.IsNullOrWhiteSpace(m_commitDescription);
                string publishButtonLabel = isBehindRemote ? "Sync & Publish" : "Publish to Remote";
                if (GUILayout.Button(publishButtonLabel, GUILayout.Height(32)))
                {
                    PublishChanges();
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2f);
            EditorGUILayout.LabelField("Note: Changes are snapshotted automatically and published together.", EditorStyles.miniLabel);
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
                int choice = EditorUtility.DisplayDialogComplex(
                    "User Identity Warning",
                    "No logged-in FlexVault user was detected. Publishing requires an active login.",
                    "Log In",
                    "Cancel",
                    "Proceed Anyway");

                if (choice == 0)
                {
                    PromptLogin();
                    return;
                }
                if (choice == 1)
                {
                    return;
                }
                // choice == 2 (Proceed Anyway): fall through and let 'fxv publish' itself decide.
            }

            var syncStatus = status?.SyncStatus;
            bool isBehind = syncStatus != null && !syncStatus.UpToDate && syncStatus.RevisionsBehind > 0;
            if (isBehind)
            {
                ulong behindCount = syncStatus.RevisionsBehind;
                string behindText = behindCount == 1 ? "1 revision" : $"{behindCount} revisions";
                bool proceedSyncPublish = EditorUtility.DisplayDialog(
                    "Sync and Publish",
                    $"Your workspace is {behindText} behind remote.\n\nFlexVault will snapshot your local changes, sync with remote to bring them up to date, and then publish.\n\nDo you want to continue?",
                    "Sync and Publish",
                    "Cancel");

                if (!proceedSyncPublish)
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
                FlexVaultAutoSnapshot.NotifySnapshotOccurred();

                if (isBehind)
                {
                    EditorUtility.DisplayProgressBar("FlexVault", "Syncing with remote...", 0.5f);
                    var syncResult = await FxvRunner.SyncAsync();
                    if (!syncResult.Success)
                    {
                        EditorUtility.DisplayDialog("Sync Failed", $"Snapshot created, but sync failed:\n\n{syncResult.ErrorMessage}\n\nYour changes remain saved as an unpublished draft.", "OK");
                        return;
                    }

                    if (syncResult.Data?.ConflictedFiles != null && syncResult.Data.ConflictedFiles.Count > 0)
                    {
                        string conflictList = string.Join("\n", syncResult.Data.ConflictedFiles);
                        EditorUtility.DisplayDialog(
                            "Sync Conflicts Detected",
                            $"Sync completed with {syncResult.Data.ConflictedFiles.Count} conflict(s):\n\n{conflictList}\n\nPlease resolve conflicts before publishing.",
                            "OK");
                        return;
                    }
                }

                EditorUtility.DisplayProgressBar("FlexVault", "Phase 2/2: Publishing draft...", 0.7f);
                var pubResult = await FxvRunner.PublishAsync(m_commitDescription);
                if (!pubResult.Success)
                {
                    string err = pubResult.ErrorMessage ?? string.Empty;
                    bool isDivergedError = err.IndexOf("diverged", StringComparison.OrdinalIgnoreCase) >= 0
                        || err.IndexOf("fxv sync", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isDivergedError)
                    {
                        bool syncNow = EditorUtility.DisplayDialog(
                            "Sync Required Before Publishing",
                            "Your changes are safely snapshotted, but the remote branch has newer commits.\n\nWould you like to sync now to bring your draft up to date?",
                            "Sync Now",
                            "Later");

                        if (syncNow)
                        {
                            // Trigger sync directly; finally block unlocks reloading and cleans progress bar
                            EditorUtility.ClearProgressBar();
                            AssetDatabase.Refresh();
                            EditorApplication.UnlockReloadAssemblies();
                            m_isOperating = false;
                            FlexVaultStateCache.RefreshAsync();
                            SyncWorkspace();
                            return;
                        }
                    }
                    else
                    {
                        bool isLoginError = err.IndexOf("no user is logged in", StringComparison.OrdinalIgnoreCase) >= 0
                            || err.IndexOf("fxv login", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isLoginError)
                        {
                            bool loginNow = EditorUtility.DisplayDialog(
                                "Publish Failed: Not Logged In",
                                "Snapshot succeeded locally, but publish failed because no FlexVault user is logged in.\n\nYour changes remain saved as an unpublished draft. Log in and publish again from this window.",
                                "Log In",
                                "Later");

                            if (loginNow)
                            {
                                PromptLogin();
                            }
                        }
                        else
                        {
                            EditorUtility.DisplayDialog(
                                "Publish Failed",
                                $"Snapshot succeeded locally, but publish failed:\n\n{pubResult.ErrorMessage}\n\nYour changes remain saved as an unpublished draft.",
                                "OK");
                        }
                    }
                    return;
                }

                m_commitDescription = string.Empty;
                ShowNotification(new GUIContent("Workspace changes were published successfully."));
                Debug.Log("[FlexVault] Workspace changes were published successfully.");
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

        private async void RevertFile(string path)
        {
            if (!EditorUtility.DisplayDialog(
                "Confirm Revert",
                $"Revert '{path}' and its companion .meta file to published base?\nAll working tree changes to this file will be lost.",
                "Revert",
                "Cancel"))
            {
                return;
            }

            if (!FlexVaultSafetyGuards.EnsureSafeToMutateWorkspace("Revert", promptSaveDirtyScenes: true)) return;

            var repoRelative = FlexVaultMetaHelper.ExpandWithMeta(new[] { path });

            m_isOperating = true;
            EditorApplication.LockReloadAssemblies();

            try
            {
                EditorUtility.DisplayProgressBar("FlexVault", "Reverting file...", 0.5f);
                var result = await FxvRunner.RevertAsync(repoRelative);
                if (!result.Success)
                {
                    EditorUtility.DisplayDialog("Revert Failed", result.ErrorMessage, "OK");
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

        private async void LoadHistoryEntries()
        {
            if (!FlexVaultSettings.IsInFlexVaultRepository()) return;
            if (FlexVaultStateCache.LatestStatus == null)
            {
                FlexVaultStateCache.RefreshAsync();
            }
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

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Space(24f + 2f);
                GUILayout.Label("Type", EditorStyles.miniBoldLabel, GUILayout.Width(75));
                GUILayout.Label("Revision", EditorStyles.miniBoldLabel, GUILayout.Width(110));
                GUILayout.Label("Author", EditorStyles.miniBoldLabel, GUILayout.Width(100));
                GUILayout.Label("Date", EditorStyles.miniBoldLabel, GUILayout.Width(110));
                GUILayout.FlexibleSpace();
                GUILayout.Label("", EditorStyles.miniBoldLabel, GUILayout.Width(65));
            }
            EditorGUILayout.EndHorizontal();

            m_historyScrollPos = EditorGUILayout.BeginScrollView(m_historyScrollPos, GUILayout.ExpandHeight(true));
            {
                for (int i = 0; i < m_historyEntries.Count; i++)
                {
                    var entry = m_historyEntries[i];
                    bool isCurrent = FlexVaultStateCache.IsCurrentWorkspaceRevision(entry, m_historyEntries);

                    var prevBg = GUI.backgroundColor;
                    if (isCurrent)
                    {
                        GUI.backgroundColor = EditorGUIUtility.isProSkin
                            ? new Color(0.20f, 0.45f, 0.28f, 1f)
                            : new Color(0.72f, 0.92f, 0.78f, 1f);
                    }
                    var bg = (i % 2 == 0) ? EditorStyles.helpBox : EditorStyles.textArea;
                    EditorGUILayout.BeginVertical(bg);

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
                            DrawExpandedChanges(entry, i, isCurrent);
                        }
                    }
                    EditorGUILayout.EndVertical();
                    GUI.backgroundColor = prevBg;
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

        private void DrawExpandedChanges(CommitRefJson entry, int commitIndex, bool isCurrent)
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
                            if (!isCurrent && GUILayout.Button("Diff vs Current", EditorStyles.miniButton, GUILayout.Width(90)))
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

        private void PromptLogin()
        {
            EditorInputDialog.Show("Log In to FlexVault", "Enter your FlexVault username:", "", (username) =>
            {
                if (!string.IsNullOrWhiteSpace(username))
                {
                    PerformLogin(username.Trim());
                }
            });
        }

        private async void PerformLogin(string username)
        {
            EditorUtility.DisplayProgressBar("FlexVault", $"Logging in as '{username}'...", 0.5f);
            try
            {
                var result = await FxvRunner.LoginAsync(username);
                if (result.Success)
                {
                    ShowNotification(new GUIContent($"Logged in as '{username}'."));
                    Debug.Log($"[FlexVault] Logged in as '{username}'.");
                    FlexVaultStateCache.RefreshAsync();
                }
                else
                {
                    EditorUtility.DisplayDialog("Log In Failed", result.ErrorMessage, "OK");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Repaint();
            }
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
                    string msg = $"Workspace state moved to '{targetRevision}'.";
                    if (HasOpenInstances<FlexVaultWindow>())
                    {
                        GetWindow<FlexVaultWindow>().ShowNotification(new GUIContent(msg));
                    }
                    else if (HasOpenInstances<FlexVaultHistoryWindow>())
                    {
                        GetWindow<FlexVaultHistoryWindow>().ShowNotification(new GUIContent(msg));
                    }
                    Debug.Log($"[FlexVault] {msg}");
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
        // Keyed by title so repeatedly clicking the button that opens one (e.g. "Go To...") just
        // refocuses the existing dialog instead of stacking up duplicates.
        private static readonly Dictionary<string, EditorInputDialog> s_openDialogs = new Dictionary<string, EditorInputDialog>();

        private string m_title;
        private string m_prompt;
        private string m_inputText;
        private Action<string> m_onConfirm;

        public static void Show(string title, string prompt, string defaultText, Action<string> onConfirm)
        {
            if (s_openDialogs.TryGetValue(title, out var existing) && existing != null)
            {
                existing.Focus();
                return;
            }

            var window = CreateInstance<EditorInputDialog>();
            window.titleContent = new GUIContent(title);
            window.m_title = title;
            window.m_prompt = prompt;
            window.m_inputText = defaultText ?? "";
            window.minSize = new Vector2(380, 130);
            window.maxSize = new Vector2(380, 130);
            window.m_onConfirm = onConfirm;
            s_openDialogs[title] = window;
            window.ShowUtility();
        }

        private void OnDestroy()
        {
            if (m_title != null && s_openDialogs.TryGetValue(m_title, out var current) && current == this)
            {
                s_openDialogs.Remove(m_title);
            }
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
