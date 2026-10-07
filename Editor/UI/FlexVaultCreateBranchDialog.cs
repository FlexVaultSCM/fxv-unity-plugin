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
            window.minSize = new Vector2(420, 260);
            window.maxSize = new Vector2(520, 300);
            s_instance = window;
            window.ShowUtility();
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        public static bool IsValidBranchName(string name, out string error)
        {
            if (string.IsNullOrEmpty(name))
            {
                error = "Branch name cannot be empty.";
                return false;
            }

            if (name.Length > MaxBranchNameLength)
            {
                error = $"Branch name cannot exceed {MaxBranchNameLength} characters.";
                return false;
            }

            if (name.Trim() != name)
            {
                error = "Branch name cannot have leading or trailing whitespace.";
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

        private void OnGUI()
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Create New Branch", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            var status = FlexVaultStateCache.LatestStatus;
            string currentUser = status?.CurrentUser;
            string currentBranch = status?.CurrentBranch ?? "unknown";

            if (string.IsNullOrEmpty(currentUser) && !m_isGlobal)
            {
                EditorGUILayout.HelpBox(
                    "No user is logged in. User branches require a logged in user. Check 'Global branch' or log in first.",
                    MessageType.Warning);
            }

            // Branch name field
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Branch Name");
            GUI.SetNextControlName("BranchNameField");
            m_branchName = EditorGUILayout.TextField(m_branchName);
            EditorGUILayout.EndHorizontal();

            if (!m_initialFocusSet)
            {
                EditorGUI.FocusTextInControl("BranchNameField");
                m_initialFocusSet = true;
            }

            // Branch naming hint
            string previewName;
            if (m_isGlobal)
            {
                previewName = !string.IsNullOrWhiteSpace(m_branchName) ? m_branchName.Trim() : "<name>";
            }
            else
            {
                string userPrefix = !string.IsNullOrEmpty(currentUser) ? currentUser : "<user>";
                previewName = !string.IsNullOrWhiteSpace(m_branchName) ? $"{userPrefix}/{m_branchName.Trim()}" : $"{userPrefix}/<name>";
            }
            EditorGUILayout.LabelField("Full branch spec:", previewName, EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            // Starting revision / source
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
                    EditorGUILayout.LabelField(" ", $"Defaults to current revision on '{currentBranch}'", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(4);

            // Options
            m_isGlobal = EditorGUILayout.Toggle(new GUIContent("Global Branch", "Create a repository-wide branch instead of a user-owned branch."), m_isGlobal);
            m_switchWorkspace = EditorGUILayout.Toggle(new GUIContent("Switch Workspace", "Move the workspace onto the new branch immediately upon creation."), m_switchWorkspace);

            // Validation message
            bool isValid = IsValidBranchName(m_branchName, out string validationError);
            if (!string.IsNullOrEmpty(m_branchName) && !isValid)
            {
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

            // Bottom action buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Width(80)))
            {
                Close();
                GUIUtility.ExitGUI();
            }

            EditorGUI.BeginDisabledGroup(m_isOperating || !isValid);
            string createButtonLabel = m_switchWorkspace ? "Create & Switch" : "Create";
            if (GUILayout.Button(createButtonLabel, GUILayout.Width(115)) || (enterPressed && isValid && !m_isOperating))
            {
                ExecuteCreateBranch();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);
        }

        private async void ExecuteCreateBranch()
        {
            string branchName = m_branchName?.Trim();
            if (!IsValidBranchName(branchName, out string validationError))
            {
                EditorUtility.DisplayDialog("Invalid Branch Name", validationError, "OK");
                return;
            }

            if (m_switchWorkspace && !FlexVaultSafetyGuards.EnsureSafeToMutateWorkspace("Create Branch", promptSaveDirtyScenes: true))
            {
                return;
            }

            m_isOperating = true;
            if (m_switchWorkspace)
            {
                EditorApplication.LockReloadAssemblies();
            }

            try
            {
                EditorUtility.DisplayProgressBar("FlexVault", $"Creating branch '{branchName}'...", 0.5f);

                string fromRev = (!m_empty && !string.IsNullOrWhiteSpace(m_fromRevision)) ? m_fromRevision.Trim() : null;
                bool noSwitch = !m_switchWorkspace;

                var result = await FxvRunner.BranchNewAsync(
                    branchName: branchName,
                    fromRevision: fromRev,
                    empty: m_empty,
                    global: m_isGlobal,
                    noSwitch: noSwitch);

                if (!result.Success)
                {
                    EditorUtility.DisplayDialog("Create Branch Failed", result.ErrorMessage ?? "Unknown error", "OK");
                    Debug.LogError($"[FlexVault] Branch creation failed: {result.ErrorMessage}");
                    return;
                }

                string createdBranch = result.Data?.Branch ?? branchName;
                string rev = result.Data?.Revision ?? "";
                string msg = $"Created branch '{createdBranch}'" + (!string.IsNullOrEmpty(rev) ? $" at {rev}." : ".");
                if (result.Data != null && result.Data.Switched)
                {
                    msg += " Workspace switched to new branch.";
                }
                Debug.Log($"[FlexVault] {msg}");

                await FlexVaultStateCache.RefreshBranchesAsync();

                if (result.Data != null && result.Data.Switched)
                {
                    AssetDatabase.Refresh();
                    FlexVaultStateCache.RefreshAsync();
                }

                Close();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Create Branch Error", ex.Message, "OK");
                Debug.LogError($"[FlexVault] Branch creation error: {ex.Message}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (m_switchWorkspace)
                {
                    EditorApplication.UnlockReloadAssemblies();
                }
                m_isOperating = false;
                Repaint();
            }
        }
    }
}
