using System;
using System.IO;
using UnityEditor;
using UnityEngine;

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
            set => EditorPrefs.SetString(BinaryPathPrefKey, value);
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
                if (Directory.Exists(Path.Combine(currentDir, ".fxv_workspace")) || Directory.Exists(Path.Combine(currentDir, ".fxv")))
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
        }

        public static bool IsInFlexVaultRepository()
        {
            string root = GetRepositoryRoot();
            return !string.IsNullOrEmpty(root) && 
                (Directory.Exists(Path.Combine(root, ".fxv_workspace")) || Directory.Exists(Path.Combine(root, ".fxv")));
        }
    }

    public class FlexVaultSettingsProvider : SettingsProvider
    {
        private string m_testStatus = string.Empty;
        private MessageType m_testMessageType = MessageType.None;
        private string m_loginUsername = string.Empty;
        private string m_authStatus = string.Empty;
        private MessageType m_authMessageType = MessageType.None;

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
            EditorGUILayout.LabelField("Repository Detected:", isRepo ? "Yes (.fxv present)" : "No (.fxv not found)");

            GUILayout.Space(10f);

            if (GUILayout.Button("Browse for fxv Executable...", GUILayout.Width(220)))
            {
                string path = EditorUtility.OpenFilePanel("Select fxv Executable", "", Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                if (!string.IsNullOrEmpty(path))
                {
                    FlexVaultSettings.CustomBinaryPath = path;
                }
            }

            GUILayout.Space(5f);

            if (GUILayout.Button("Test CLI Connection", GUILayout.Width(220)))
            {
                TestConnection();
            }

            if (!string.IsNullOrEmpty(m_testStatus))
            {
                GUILayout.Space(5f);
                EditorGUILayout.HelpBox(m_testStatus, m_testMessageType);
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
                    PerformLogout();
                }
            }
            else
            {
                EditorGUILayout.LabelField("Status: Not logged in (commits will require login)", EditorStyles.miniLabel);
                GUILayout.Space(3f);
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Username:", GUILayout.Width(80));
                    m_loginUsername = EditorGUILayout.TextField(m_loginUsername, GUILayout.Width(200));
                    GUI.enabled = !string.IsNullOrWhiteSpace(m_loginUsername);
                    if (GUILayout.Button("Log In", GUILayout.Width(100)))
                    {
                        PerformLogin(m_loginUsername.Trim());
                    }
                    GUI.enabled = true;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(m_authStatus))
            {
                GUILayout.Space(5f);
                EditorGUILayout.HelpBox(m_authStatus, m_authMessageType);
            }
        }

        private async void PerformLogin(string username)
        {
            m_authStatus = $"Logging in as '{username}'...";
            m_authMessageType = MessageType.Info;
            SettingsService.NotifySettingsProviderChanged();

            var result = await FxvRunner.LoginAsync(username);
            if (result.Success)
            {
                m_authStatus = $"Logged in successfully as '{username}'.";
                m_authMessageType = MessageType.Info;
                m_loginUsername = string.Empty;
                FlexVaultStateCache.RefreshAsync();
            }
            else
            {
                m_authStatus = $"Login failed: {result.ErrorMessage}";
                m_authMessageType = MessageType.Error;
            }
            SettingsService.NotifySettingsProviderChanged();
        }

        private async void PerformLogout()
        {
            m_authStatus = "Logging out...";
            m_authMessageType = MessageType.Info;
            SettingsService.NotifySettingsProviderChanged();

            var result = await FxvRunner.LogoutAsync();
            if (result.Success)
            {
                m_authStatus = "Logged out successfully.";
                m_authMessageType = MessageType.Info;
                FlexVaultStateCache.RefreshAsync();
            }
            else
            {
                m_authStatus = $"Logout failed: {result.ErrorMessage}";
                m_authMessageType = MessageType.Error;
            }
            SettingsService.NotifySettingsProviderChanged();
        }

        private async void TestConnection()
        {
            m_testStatus = "Testing connection to fxv CLI...";
            m_testMessageType = MessageType.Info;
            SettingsService.NotifySettingsProviderChanged();

            var result = await FxvRunner.RunCommandAsync<StatusPayload>(new[] { "status", "--skip-remote-update", "--skip-scan" });
            if (result.Success)
            {
                m_testStatus = $"Successfully connected to fxv!\nBranch: {result.Data?.CurrentBranch ?? "unknown"}\nUser: {result.Data?.CurrentUser ?? "Logged out"}";
                m_testMessageType = MessageType.Info;
            }
            else
            {
                m_testStatus = $"Failed to connect: {result.ErrorMessage}";
                m_testMessageType = MessageType.Error;
            }
            SettingsService.NotifySettingsProviderChanged();
        }
    }
}
