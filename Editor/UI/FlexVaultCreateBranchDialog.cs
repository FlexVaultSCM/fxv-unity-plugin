using System;
using System.Text.RegularExpressions;
using FlexVault.VCS.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.UI
{
    public class FlexVaultCreateBranchDialog : EditorWindow
    {
        private static readonly Regex s_branchNameRegex = new Regex(@"^[a-zA-Z0-9_\- ]+$", RegexOptions.Compiled);
        private const int MaxBranchNameLength = 64;

        private static FlexVaultCreateBranchDialog s_instance;

        private string m_branchName = "";
        private string m_fromRevision = "";
        private bool m_empty = false;
        private bool m_isGlobal = false;
        private bool m_switchWorkspace = true;
        private bool m_isOperating = false;
        private bool m_initialFocusSet = false;

        private GUIStyle m_specBoxStyle;
        private GUIStyle m_specLabelStyle;
        private GUIStyle m_specBadgeStyle;

        private void EnsureStyles()
        {
            if (m_specBoxStyle == null)
            {
                m_specBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(8, 8, 3, 3),
                    margin = new RectOffset(0, 0, 1, 1)
                };
            }
            if (m_specLabelStyle == null)
            {
                m_specLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 11,
                    alignment = TextAnchor.MiddleLeft
                };
            }
            if (m_specBadgeStyle == null)
            {
                m_specBadgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleRight
                };
            }
        }

        public static void ShowWindow(string fromRevision = null)
        {
            if (s_instance != null)
            {
                if (!string.IsNullOrEmpty(fromRevision))
                {
                    s_instance.m_fromRevision = fromRevision;
                    s_instance.m_empty = false;
                }
                s_instance.Focus();
                return;
            }

            var window = CreateInstance<FlexVaultCreateBranchDialog>();
            window.titleContent = new GUIContent("Create Branch");
            window.m_fromRevision = fromRevision ?? "";
            const float windowWidth = 450f;
            const float windowHeight = 410f;
            window.minSize = new Vector2(windowWidth, windowHeight);
            window.maxSize = new Vector2(windowWidth, windowHeight);
            s_instance = window;
            window.ShowUtility();
        }

        private void OnEnable()
        {
            s_instance = this;
            FlexVaultStateCache.OnStateChanged += OnStateCacheChanged;
            if (FlexVaultStateCache.LatestStatus == null)
            {
                FlexVaultStateCache.RefreshAsync(skipScan: true);
            }
        }

        private void OnDisable()
        {
            FlexVaultStateCache.OnStateChanged -= OnStateCacheChanged;
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        private void OnStateCacheChanged()
        {
            if (this != null)
            {
                Repaint();
            }
        }

        public static bool IsValidBranchName(string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Branch name cannot be empty.";
                return false;
            }

            if (name.Trim() != name)
            {
                error = "Branch name cannot have leading or trailing whitespace.";
                return false;
            }

            if (name.Length > MaxBranchNameLength)
            {
                error = $"Branch name cannot exceed {MaxBranchNameLength} characters.";
                return false;
            }

            if (name.Contains("/"))
            {
                error = "Enter only the branch name. Do not include a username prefix or slash.";
                return false;
            }

            if (!s_branchNameRegex.IsMatch(name))
            {
                error = "Branch name can only contain letters, digits, underscores, hyphens, and spaces.";
                return false;
            }

            error = null;
            return true;
        }

        private static void DrawSeparator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            rect.height = 1;
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.72f, 0.72f, 0.72f));
        }

        private void OnGUI()
        {
            var status = FlexVaultStateCache.LatestStatus;
            string currentUser = status?.CurrentUser;
            string currentBranch = status?.CurrentBranch ?? "unknown";
            string currentRevision = status?.HeadCommit?.LocalSnapshot?.RevisionDisplay
                ?? (status?.SyncStatus?.SyncedRevision != null && status?.CurrentBranch != null
                    ? $"{status.CurrentBranch}.{status.SyncStatus.SyncedRevision.Value}"
                    : null);

            bool requiresLogin = string.IsNullOrEmpty(currentUser) && !m_isGlobal;
            bool isValid = IsValidBranchName(m_branchName, out string validationError);
            bool canSubmit = isValid && !requiresLogin && !m_isOperating;

            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(16, 16, 14, 14) });
            {
                // Header
                EditorGUILayout.BeginHorizontal();
                {
                    var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
                    GUILayout.Label("Create Branch", headerStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"Current: {currentBranch}", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("Create a new branch in the FlexVault repository.", EditorStyles.miniLabel);
                EditorGUILayout.Space(6);
                DrawSeparator();
                EditorGUILayout.Space(8);

                // Disable all form inputs while operating
                EditorGUI.BeginDisabledGroup(m_isOperating);
                {
                    // Section 1: Branch Details Card
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    {
                        GUILayout.Label("Branch Details", EditorStyles.boldLabel);
                        EditorGUILayout.Space(4);

                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.PrefixLabel("Branch Name");
                            GUI.SetNextControlName("BranchNameField");
                            m_branchName = EditorGUILayout.TextField(m_branchName);
                        }
                        EditorGUILayout.EndHorizontal();

                        if (!m_initialFocusSet)
                        {
                            EditorGUI.FocusTextInControl("BranchNameField");
                            m_initialFocusSet = true;
                        }

                        // Full branch preview row
                        string userPrefix = !string.IsNullOrEmpty(currentUser) ? currentUser : "<user>";
                        string previewName = m_isGlobal
                            ? (!string.IsNullOrWhiteSpace(m_branchName) ? m_branchName.Trim() : "<name>")
                            : (!string.IsNullOrWhiteSpace(m_branchName) ? $"{userPrefix}/{m_branchName.Trim()}" : $"{userPrefix}/<name>");

                        EditorGUILayout.Space(3);
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.PrefixLabel(new GUIContent("Full Spec", "The fully qualified branch identifier that will be created in FlexVault."));

                            EnsureStyles();
                            EditorGUILayout.BeginHorizontal(m_specBoxStyle, GUILayout.Height(22));
                            {
                                GUILayout.Label(previewName, m_specLabelStyle);
                                GUILayout.FlexibleSpace();

                                Color badgeColor = m_isGlobal
                                    ? (EditorGUIUtility.isProSkin ? new Color(0.4f, 0.75f, 1f) : new Color(0.1f, 0.45f, 0.85f))
                                    : (EditorGUIUtility.isProSkin ? new Color(0.95f, 0.7f, 0.25f) : new Color(0.75f, 0.45f, 0.1f));

                                Color prevContentColor = GUI.contentColor;
                                GUI.contentColor = badgeColor;
                                string badgeText = m_isGlobal ? "global" : $"user ({userPrefix})";
                                GUILayout.Label(badgeText, m_specBadgeStyle);
                                GUI.contentColor = prevContentColor;
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.Space(6);

                    // Section 2: Start Point Card
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    {
                        GUILayout.Label("Starting Point", EditorStyles.boldLabel);
                        EditorGUILayout.Space(4);

                        m_empty = EditorGUILayout.Toggle(new GUIContent("Empty Branch", "Start branch with no parent history or content."), m_empty);

                        if (m_empty)
                        {
                            EditorGUI.BeginDisabledGroup(true);
                            EditorGUILayout.TextField("Start Revision", "(none - starts empty)");
                            EditorGUI.EndDisabledGroup();
                        }
                        else
                        {
                            m_fromRevision = EditorGUILayout.TextField(new GUIContent("Start Revision", "Revision spec to branch from. Leave blank for current workspace revision."), m_fromRevision);
                            if (string.IsNullOrWhiteSpace(m_fromRevision))
                            {
                                string baseLabel = !string.IsNullOrEmpty(currentRevision) ? $"({currentRevision})" : $"on '{currentBranch}'";
                                EditorGUILayout.LabelField(" ", $"Defaults to current revision {baseLabel}", EditorStyles.miniLabel);
                            }
                        }
                    }
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.Space(6);

                    // Section 3: Options Card
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    {
                        GUILayout.Label("Options", EditorStyles.boldLabel);
                        EditorGUILayout.Space(4);

                        m_isGlobal = EditorGUILayout.Toggle(new GUIContent("Global Branch", "Create a repository-wide branch visible across the project instead of user-owned."), m_isGlobal);
                        m_switchWorkspace = EditorGUILayout.Toggle(new GUIContent("Switch Workspace", "Move workspace to the new branch upon creation."), m_switchWorkspace);
                    }
                    EditorGUILayout.EndVertical();
                }
                EditorGUI.EndDisabledGroup();

                // Validation or Login Warning
                if (requiresLogin)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox("No user is logged in. User branches require a logged-in user. Check 'Global Branch' or log in first.", MessageType.Warning);
                }
                else if (!string.IsNullOrEmpty(m_branchName) && !isValid)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(validationError, MessageType.Warning);
                }

                GUILayout.FlexibleSpace();

                // Keyboard shortcut Enter
                bool enterPressed = false;
                Event e = Event.current;
                if (e != null && e.isKey && e.keyCode == KeyCode.Return && e.type == EventType.KeyDown)
                {
                    enterPressed = true;
                    e.Use();
                }

                // Action Buttons Footer
                DrawSeparator();
                EditorGUILayout.Space(8);

                EditorGUILayout.BeginHorizontal();
                {
                    GUILayout.FlexibleSpace();

                    GUI.enabled = !m_isOperating;
                    if (GUILayout.Button("Cancel", GUILayout.Width(85), GUILayout.Height(24)))
                    {
                        Close();
                        GUIUtility.ExitGUI();
                    }
                    GUI.enabled = true;

                    GUILayout.Space(6);

                    EditorGUI.BeginDisabledGroup(!canSubmit);
                    string createButtonLabel = m_switchWorkspace ? "Create & Switch" : "Create";
                    if (GUILayout.Button(createButtonLabel, GUILayout.Width(125), GUILayout.Height(24)) || (enterPressed && canSubmit))
                    {
                        ExecuteCreateBranch();
                    }
                    EditorGUI.EndDisabledGroup();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private async void ExecuteCreateBranch()
        {
            if (!IsValidBranchName(m_branchName, out string validationError))
            {
                EditorUtility.DisplayDialog("Invalid Branch Name", validationError, "OK");
                return;
            }

            string branchName = m_branchName.Trim();

            var status = FlexVaultStateCache.LatestStatus;
            string currentUser = status?.CurrentUser;
            if (string.IsNullOrEmpty(currentUser) && !m_isGlobal)
            {
                EditorUtility.DisplayDialog("Login Required", "No user is logged in. Please check 'Global Branch' or log in with 'fxv login' before creating a user branch.", "OK");
                return;
            }

            if (m_switchWorkspace && !FlexVaultSafetyGuards.EnsureSafeToMutateWorkspace("Create Branch", promptSaveDirtyScenes: true))
            {
                return;
            }

            m_isOperating = true;
            bool lockedAssemblies = false;
            if (m_switchWorkspace)
            {
                EditorApplication.LockReloadAssemblies();
                lockedAssemblies = true;
            }

            try
            {
                EditorUtility.DisplayProgressBar("FlexVault", $"Creating branch '{branchName}'...", 0.5f);

                string fromRev = (!m_empty && !string.IsNullOrWhiteSpace(m_fromRevision)) ? m_fromRevision.Trim() : null;

                // If starting from a specific revision different from the current workspace revision,
                // the fxv CLI requires --no-switch during creation, followed by a branch switch / goto.
                string currentRevision = status?.HeadCommit?.LocalSnapshot?.RevisionDisplay
                    ?? (status?.SyncStatus?.SyncedRevision != null && status?.CurrentBranch != null
                        ? $"{status.CurrentBranch}.{status.SyncStatus.SyncedRevision.Value}"
                        : null);

                bool isHistoricalRevision = fromRev != null && !string.Equals(fromRev, currentRevision, StringComparison.OrdinalIgnoreCase);
                bool needTwoStepSwitch = m_switchWorkspace && isHistoricalRevision;
                bool createNoSwitch = !m_switchWorkspace || needTwoStepSwitch;

                var result = await FxvRunner.BranchNewAsync(
                    branchName: branchName,
                    fromRevision: fromRev,
                    empty: m_empty,
                    global: m_isGlobal,
                    noSwitch: createNoSwitch);

                if (!result.Success)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("Create Branch Failed", result.ErrorMessage ?? "Unknown error", "OK");
                    Debug.LogError($"[FlexVault] Branch creation failed: {result.ErrorMessage}");
                    return;
                }

                string createdBranch = result.Data?.Branch ?? branchName;
                bool switchedSuccessfully = result.Data != null && result.Data.Switched;

                // Step 2: If branching from a historical revision and switch was requested, switch to it now
                if (needTwoStepSwitch)
                {
                    EditorUtility.DisplayProgressBar("FlexVault", $"Switching workspace to '{createdBranch}'...", 0.8f);
                    var switchResult = await FxvRunner.BranchSwitchAsync(createdBranch);
                    if (!switchResult.Success)
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog(
                            "Branch Created, Switch Failed",
                            $"Branch '{createdBranch}' was created successfully, but switching workspace to it failed:\n\n{switchResult.ErrorMessage}",
                            "OK");
                        Debug.LogWarning($"[FlexVault] Branch switch failed: {switchResult.ErrorMessage}");
                    }
                    else
                    {
                        switchedSuccessfully = true;
                    }
                }

                string rev = result.Data?.Revision ?? "";
                string msg = $"Created branch '{createdBranch}'" + (!string.IsNullOrEmpty(rev) ? $" at {rev}." : ".");
                if (switchedSuccessfully)
                {
                    msg += " Workspace switched to new branch.";
                }
                Debug.Log($"[FlexVault] {msg}");

                await FlexVaultStateCache.RefreshBranchesAsync();

                if (switchedSuccessfully)
                {
                    AssetDatabase.Refresh();
                    FlexVaultStateCache.RefreshAsync();
                }

                Close();
            }
            catch (OperationCanceledException)
            {
                // Assembly/domain reload in progress; exit cleanly
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Create Branch Error", ex.Message, "OK");
                Debug.LogError($"[FlexVault] Branch creation error: {ex.Message}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (lockedAssemblies)
                {
                    EditorApplication.UnlockReloadAssemblies();
                }
                m_isOperating = false;
                if (this != null)
                {
                    Repaint();
                }
            }
        }
    }
}
