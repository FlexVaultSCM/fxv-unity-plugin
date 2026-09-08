using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using FlexVault.VCS.Editor.UI;

namespace FlexVault.VCS.Editor.Core
{
    public static class FlexVaultSettings
    {
        private const string BinaryPathPrefKey = "FlexVault_BinaryPath";
        private static string s_cachedRepoRoot;
        private static bool s_repoRootSearched;

        public static string CustomBinaryPath
        {
            get => EditorPrefs.GetString(BinaryPathPrefKey, string.Empty);
            set
            {
                EditorPrefs.SetString(BinaryPathPrefKey, value);
                FlexVaultVersionGuard.ResetCachedVersion();
            }
        }

        public static string GetEffectiveBinaryPath()
        {
            string custom = CustomBinaryPath;
            if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
            {
                return custom;
            }

            string autoDiscovered = AutoDiscoverBinary();
            if (!string.IsNullOrWhiteSpace(autoDiscovered))
            {
                return autoDiscovered;
            }

            return Application.platform == RuntimePlatform.WindowsEditor ? "fxv.exe" : "fxv";
        }

        public static string AutoDiscoverBinary()
        {
            string binaryName = Application.platform == RuntimePlatform.WindowsEditor ? "fxv.exe" : "fxv";

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string standardPath = Path.Combine(localAppData, "fxv", "bin", binaryName);
                if (File.Exists(standardPath))
                {
                    return standardPath;
                }

                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string pfPath = Path.Combine(programFiles, "FlexVault", "bin", binaryName);
                if (File.Exists(pfPath))
                {
                    return pfPath;
                }
            }
            else
            {
                string[] standardUnixPaths = {
                    Path.Combine("/usr/local/bin", binaryName),
                    Path.Combine("/opt/homebrew/bin", binaryName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", binaryName)
                };

                foreach (string path in standardUnixPaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                char separator = Application.platform == RuntimePlatform.WindowsEditor ? ';' : ':';
                string[] dirs = pathEnv.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string dir in dirs)
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim(), binaryName);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                        // Ignore invalid paths in PATH
                    }
                }
            }

            return null;
        }

        public static string GetRepositoryRoot()
        {
            if (s_repoRootSearched && !string.IsNullOrEmpty(s_cachedRepoRoot))
            {
                return s_cachedRepoRoot;
            }

            string currentDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            while (!string.IsNullOrEmpty(currentDir))
            {
                if (Directory.Exists(Path.Combine(currentDir, ".fxv_workspace")))
                {
                    s_cachedRepoRoot = currentDir.Replace('\\', '/');
                    s_repoRootSearched = true;
                    return s_cachedRepoRoot;
                }

                DirectoryInfo parent = Directory.GetParent(currentDir);
                if (parent == null)
                {
                    break;
                }
                currentDir = parent.FullName;
            }

            s_cachedRepoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            s_repoRootSearched = true;
            return s_cachedRepoRoot;
        }

        public static void InvalidateRepoRoot()
        {
            s_repoRootSearched = false;
            s_cachedRepoRoot = null;
            FlexVaultMetaHelper.InvalidateProjectRoot();
        }

        public static bool IsInFlexVaultRepository()
        {
            string root = GetRepositoryRoot();
            return !string.IsNullOrEmpty(root) && Directory.Exists(Path.Combine(root, ".fxv_workspace"));
        }
    }

    public class FlexVaultSettingsUIState
    {
        public string TestStatus = string.Empty;
        public MessageType TestMessageType = MessageType.None;
        public string LoginUsername = string.Empty;
        public string AuthStatus = string.Empty;
        public MessageType AuthMessageType = MessageType.None;
    }

    public static class FlexVaultSettingsDrawer
    {
        public static void DrawSettings(FlexVaultSettingsUIState state, Action repaintCallback = null)
        {
            if (state == null) return;

            GUILayout.Space(10f);
            EditorGUILayout.LabelField("FlexVault Version Control Settings", EditorStyles.boldLabel);
            GUILayout.Space(5f);

            string currentCustom = FlexVaultSettings.CustomBinaryPath;
            EditorGUI.BeginChangeCheck();
            string newCustom = EditorGUILayout.TextField("Custom CLI Executable Path", currentCustom);
            if (EditorGUI.EndChangeCheck())
            {
                FlexVaultSettings.CustomBinaryPath = newCustom;
            }

            GUILayout.Space(5f);
            string effectivePath = FlexVaultSettings.GetEffectiveBinaryPath();
            EditorGUILayout.LabelField("Resolved CLI Executable:", effectivePath ?? "Not found", EditorStyles.wordWrappedLabel);

            string repoRoot = FlexVaultSettings.GetRepositoryRoot();
            bool isRepo = FlexVaultSettings.IsInFlexVaultRepository();
            EditorGUILayout.LabelField("Repository Root:", repoRoot ?? "Unknown", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Repository Detected:", isRepo ? "Yes (.fxv_workspace present)" : "No (.fxv_workspace not found)");

            GUILayout.Space(10f);

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("Browse for fxv Executable...", GUILayout.Width(220)))
                {
                    string path = EditorUtility.OpenFilePanel("Select fxv Executable", "", Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        FlexVaultSettings.CustomBinaryPath = path;
                        repaintCallback?.Invoke();
                    }
                }

                if (GUILayout.Button("Open FlexVault Window", GUILayout.Width(180)))
                {
                    EditorApplication.delayCall += () =>
                    {
                        FlexVaultWindow.ShowWindow();
                    };
                }
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5f);

            if (GUILayout.Button("Test CLI Connection", GUILayout.Width(220)))
            {
                TestConnection(state, repaintCallback);
            }

            if (!string.IsNullOrEmpty(state.TestStatus))
            {
                GUILayout.Space(5f);
                EditorGUILayout.HelpBox(state.TestStatus, state.TestMessageType);
            }

            GUILayout.Space(15f);
            EditorGUILayout.LabelField("User Identity & Authentication", EditorStyles.boldLabel);
            GUILayout.Space(5f);

            var status = FlexVaultStateCache.LatestStatus;
            string currentUser = status?.CurrentUser;
            bool isLoggedIn = !string.IsNullOrEmpty(currentUser);

            if (isLoggedIn)
            {
                EditorGUILayout.LabelField("Logged in as:", currentUser, EditorStyles.boldLabel);
                GUILayout.Space(5f);
                if (GUILayout.Button("Log Out", GUILayout.Width(120)))
                {
                    PerformLogout(state, repaintCallback);
                }
            }
            else
            {
                EditorGUILayout.LabelField("Status: Not logged in (commits will require login)", EditorStyles.miniLabel);
                GUILayout.Space(3f);
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Username:", GUILayout.Width(80));
                    state.LoginUsername = EditorGUILayout.TextField(state.LoginUsername, GUILayout.Width(200));
                    GUI.enabled = !string.IsNullOrWhiteSpace(state.LoginUsername);
                    if (GUILayout.Button("Log In", GUILayout.Width(100)))
                    {
                        PerformLogin(state.LoginUsername.Trim(), state, repaintCallback);
                    }
                    GUI.enabled = true;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(state.AuthStatus))
            {
                GUILayout.Space(5f);
                EditorGUILayout.HelpBox(state.AuthStatus, state.AuthMessageType);
            }
        }

        private static async void PerformLogin(string username, FlexVaultSettingsUIState state, Action repaintCallback)
        {
            state.AuthStatus = $"Logging in as '{username}'...";
            state.AuthMessageType = MessageType.Info;
            SettingsService.NotifySettingsProviderChanged();
            repaintCallback?.Invoke();

            var result = await FxvRunner.LoginAsync(username);
            if (result.Success)
            {
                state.AuthStatus = $"Logged in successfully as '{username}'.";
                state.AuthMessageType = MessageType.Info;
                state.LoginUsername = string.Empty;
                FlexVaultStateCache.RefreshAsync();
            }
            else
            {
                state.AuthStatus = $"Login failed: {result.ErrorMessage}";
                state.AuthMessageType = MessageType.Error;
            }
            SettingsService.NotifySettingsProviderChanged();
            repaintCallback?.Invoke();
        }

        private static async void PerformLogout(FlexVaultSettingsUIState state, Action repaintCallback)
        {
            state.AuthStatus = "Logging out...";
            state.AuthMessageType = MessageType.Info;
            SettingsService.NotifySettingsProviderChanged();
            repaintCallback?.Invoke();

            var result = await FxvRunner.LogoutAsync();
            if (result.Success)
            {
                state.AuthStatus = "Logged out successfully.";
                state.AuthMessageType = MessageType.Info;
                FlexVaultStateCache.RefreshAsync();
            }
            else
            {
                state.AuthStatus = $"Logout failed: {result.ErrorMessage}";
                state.AuthMessageType = MessageType.Error;
            }
            SettingsService.NotifySettingsProviderChanged();
            repaintCallback?.Invoke();
        }

        private static async void TestConnection(FlexVaultSettingsUIState state, Action repaintCallback)
        {
            state.TestStatus = "Testing connection to fxv CLI...";
            state.TestMessageType = MessageType.Info;
            SettingsService.NotifySettingsProviderChanged();
            repaintCallback?.Invoke();

            var result = await FxvRunner.RunCommandAsync<StatusPayload>(new[] { "status", "--skip-remote-update", "--skip-scan" });
            if (result.Success)
            {
                state.TestStatus = $"Successfully connected to fxv!\nBranch: {result.Data?.CurrentBranch ?? "unknown"}\nUser: {result.Data?.CurrentUser ?? "Logged out"}";
                state.TestMessageType = MessageType.Info;
            }
            else
            {
                state.TestStatus = $"Failed to connect: {result.ErrorMessage}";
                state.TestMessageType = MessageType.Error;
            }
            SettingsService.NotifySettingsProviderChanged();
            repaintCallback?.Invoke();
        }
    }

    public class FlexVaultSettingsProvider : SettingsProvider
    {
        private readonly FlexVaultSettingsUIState m_uiState = new FlexVaultSettingsUIState();

        public FlexVaultSettingsProvider(string path, SettingsScope scope) : base(path, scope) { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new FlexVaultSettingsProvider("Project/Version Control/FlexVault", SettingsScope.Project)
            {
                keywords = new[] { "FlexVault", "VCS", "SCM", "Source Control", "fxv" }
            };
        }

        public override void OnGUI(string searchContext)
        {
            FlexVaultSettingsDrawer.DrawSettings(m_uiState, SettingsService.NotifySettingsProviderChanged);
        }
    }
}
