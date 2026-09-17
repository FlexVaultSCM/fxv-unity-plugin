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

        private const string IntegrationEnabledPrefKey = "FlexVault_IntegrationEnabled";

        public static bool IntegrationEnabled
        {
            get => EditorPrefs.GetBool(IntegrationEnabledPrefKey, true);
            set => EditorPrefs.SetBool(IntegrationEnabledPrefKey, value);
        }

        private const string PeriodicSnapshotIntervalPrefKey = "FlexVault_PeriodicSnapshotIntervalSeconds";
        public const int DefaultPeriodicSnapshotIntervalSeconds = 300;

        /// <summary>
        /// How long the workspace can sit with pending changes and no snapshot before an automatic
        /// one is taken. Zero or negative disables the periodic snapshot entirely.
        /// </summary>
        public static int PeriodicSnapshotIntervalSeconds
        {
            get => EditorPrefs.GetInt(PeriodicSnapshotIntervalPrefKey, DefaultPeriodicSnapshotIntervalSeconds);
            set => EditorPrefs.SetInt(PeriodicSnapshotIntervalPrefKey, value);
        }

        public static bool IsInFlexVaultRepository()
        {
            string root = GetRepositoryRoot();
            return !string.IsNullOrEmpty(root) && Directory.Exists(Path.Combine(root, ".fxv_workspace"));
        }

        public static bool IsFlexVaultActive()
        {
            if (!IntegrationEnabled)
            {
                return false;
            }

            if (!IsInFlexVaultRepository())
            {
                return false;
            }

            var activeVcs = UnityEditor.VersionControl.VersionControlManager.activeVersionControlObject;
            if (activeVcs != null && !(activeVcs is FlexVaultVersionControlObject))
            {
                return false;
            }

            return true;
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

            if (FlexVaultVersionGuard.IsVersionCompatible == false)
            {
                EditorGUILayout.HelpBox(FlexVaultVersionGuard.LastErrorMessage ?? "Incompatible FlexVault CLI version.", MessageType.Error);
            }
            else if (FlexVaultVersionGuard.IsVersionCompatible == true && !string.IsNullOrEmpty(FlexVaultVersionGuard.LastVersionString))
            {
                EditorGUILayout.HelpBox($"FlexVault CLI v{FlexVaultVersionGuard.LastVersionString} is compatible.", MessageType.Info);
            }

            string repoRoot = FlexVaultSettings.GetRepositoryRoot();
            bool isRepo = FlexVaultSettings.IsInFlexVaultRepository();
            EditorGUILayout.LabelField("Repository Root:", repoRoot ?? "Unknown", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Repository Detected:", isRepo ? "Yes (.fxv_workspace present)" : "No (.fxv_workspace not found)");

            GUILayout.Space(15f);
            EditorGUILayout.LabelField("Automatic Snapshots", EditorStyles.boldLabel);
            GUILayout.Space(5f);

            EditorGUI.BeginChangeCheck();
            int newInterval = EditorGUILayout.IntField("Periodic Snapshot Interval (seconds)", FlexVaultSettings.PeriodicSnapshotIntervalSeconds);
            if (EditorGUI.EndChangeCheck())
            {
                FlexVaultSettings.PeriodicSnapshotIntervalSeconds = newInterval;
            }
            EditorGUILayout.LabelField(
                FlexVaultSettings.PeriodicSnapshotIntervalSeconds > 0
                    ? "A snapshot is taken automatically if pending changes have gone this long without one. Set to 0 to disable."
                    : "Periodic snapshots are disabled.",
                EditorStyles.wordWrappedMiniLabel);

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

            FlexVaultVersionGuard.ResetCachedVersion();
            await FxvRunner.EnsureVersionCheckedAsync();

            var result = await FxvRunner.RunCommandAsync<StatusPayload>(new[] { "status", "--skip-remote-update", "--skip-scan" });
            if (result.Success)
            {
                state.TestStatus = $"Successfully connected to fxv!\nVersion: {FlexVaultVersionGuard.LastVersionString ?? "unknown"}\nBranch: {result.Data?.CurrentBranch ?? "unknown"}\nUser: {result.Data?.CurrentUser ?? "Logged out"}";
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

}
