using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;

[assembly: InternalsVisibleTo("FlexVault.VCS.Editor.Tests")]

namespace FlexVault.VCS.Editor.Core
{
    [Serializable]
    public class RemoteReleaseManifest
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("plugin_version")]
        public string PluginVersion { get; set; }

        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("cli_compatibility")]
        public RemoteCliCompatibility CliCompatibility { get; set; }
    }

    [Serializable]
    public class RemoteCliCompatibility
    {
        [JsonProperty("min")]
        public string Min { get; set; }

        [JsonProperty("max")]
        public string Max { get; set; }
    }
    public struct FxvCliVersion : IComparable<FxvCliVersion>, IEquatable<FxvCliVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public FxvCliVersion(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public static bool TryParse(string versionStr, out FxvCliVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(versionStr))
            {
                return false;
            }

            string[] parts = versionStr.Trim().Split('.');
            if (parts.Length < 2 || parts.Length > 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
            {
                return false;
            }

            int patch = 0;
            if (parts.Length >= 3)
            {
                string patchStr = parts[2];
                int dashIdx = patchStr.IndexOf('-');
                if (dashIdx >= 0)
                {
                    patchStr = patchStr.Substring(0, dashIdx);
                }
                int plusIdx = patchStr.IndexOf('+');
                if (plusIdx >= 0)
                {
                    patchStr = patchStr.Substring(0, plusIdx);
                }

                if (!int.TryParse(patchStr, out patch))
                {
                    return false;
                }
            }

            version = new FxvCliVersion(major, minor, patch);
            return true;
        }

        public int CompareTo(FxvCliVersion other)
        {
            if (Major != other.Major) return Major.CompareTo(other.Major);
            if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
            return Patch.CompareTo(other.Patch);
        }

        public bool Equals(FxvCliVersion other)
        {
            return Major == other.Major && Minor == other.Minor && Patch == other.Patch;
        }

        public override bool Equals(object obj)
        {
            return obj is FxvCliVersion other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Major, Minor, Patch);
        }

        public override string ToString()
        {
            return $"{Major}.{Minor}.{Patch}";
        }

        public static bool operator <(FxvCliVersion a, FxvCliVersion b) => a.CompareTo(b) < 0;
        public static bool operator <=(FxvCliVersion a, FxvCliVersion b) => a.CompareTo(b) <= 0;
        public static bool operator >(FxvCliVersion a, FxvCliVersion b) => a.CompareTo(b) > 0;
        public static bool operator >=(FxvCliVersion a, FxvCliVersion b) => a.CompareTo(b) >= 0;
        public static bool operator ==(FxvCliVersion a, FxvCliVersion b) => a.Equals(b);
        public static bool operator !=(FxvCliVersion a, FxvCliVersion b) => !a.Equals(b);
    }

    public static class FlexVaultVersionGuard
    {
        // Pinned compatible range: [MinVersion, MaxVersion)
        // MinVersion tracks the `status` command's payload schema; versions below it emit a shape
        // this plugin no longer parses.
        public static readonly FxvCliVersion MinVersion = new FxvCliVersion(0, 11, 0);
        public static readonly FxvCliVersion MaxVersion = new FxvCliVersion(0, 14, 0);
        public static readonly FxvCliVersion FetchSupportedVersion = new FxvCliVersion(0, 12, 0);

        public static bool? IsVersionCompatible => s_isVersionCompatible;
        public static string LastVersionString => s_lastVersionString;
        public static string LastErrorMessage => s_lastErrorMessage;
        public static FxvCliVersion? CachedVersion => s_cachedVersion;
        public static bool SupportsFetch => s_isVersionCompatible == true && s_cachedVersion.HasValue && s_cachedVersion.Value >= FetchSupportedVersion;

        private static bool? s_isVersionCompatible;
        private static string s_lastVersionString;
        private static string s_lastErrorMessage;
        private static FxvCliVersion? s_cachedVersion;

        public const string RemoteVersionJsonUrl = "https://github.com/FlexVaultSCM/fxv-unity-plugin/releases/latest/download/version.json";
        public const string DocumentationUrl = "https://docs.fxv.dev/how-to/unity-plugin/";
        public static string AvailableUpdateVersion { get; private set; }
        public static string AvailableUpdateCliRange { get; private set; }
        public static event Action OnAvailableUpdateChanged;

        private static bool s_isCheckingRemoteUpdate;
        private static double s_lastRemoteCheckTime = -1;
        private static string s_lastRemoteManifestJson;
        private const double RemoteCheckIntervalSeconds = 3600.0; // 1 hour
        private static readonly int s_mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

        public static void ResetCachedVersion()
        {
            s_isVersionCompatible = null;
            s_lastVersionString = null;
            s_lastErrorMessage = null;
            s_cachedVersion = null;
            AvailableUpdateVersion = null;
            AvailableUpdateCliRange = null;
            s_lastRemoteManifestJson = null;
            s_lastRemoteCheckTime = -1;
            s_pluginVersion = null;
            NotifyUpdateChanged();
        }

        private static void NotifyUpdateChanged()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == s_mainThreadId)
            {
                OnAvailableUpdateChanged?.Invoke();
            }
            else
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    OnAvailableUpdateChanged?.Invoke();
                };
            }
        }

        public static void CheckForRemoteUpdateAsync()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != s_mainThreadId)
            {
                UnityEditor.EditorApplication.delayCall += () => CheckForRemoteUpdateAsync();
                return;
            }

            try
            {
                double now = UnityEditor.EditorApplication.timeSinceStartup;
                if (s_isCheckingRemoteUpdate || (s_lastRemoteCheckTime >= 0 && now - s_lastRemoteCheckTime < RemoteCheckIntervalSeconds))
                {
                    return;
                }

                s_isCheckingRemoteUpdate = true;
                s_lastRemoteCheckTime = now;

                var request = UnityEngine.Networking.UnityWebRequest.Get(RemoteVersionJsonUrl);
                request.timeout = 10;
                var asyncOp = request.SendWebRequest();
                asyncOp.completed += _ =>
                {
                    try
                    {
                        if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success && !string.IsNullOrEmpty(request.downloadHandler.text))
                        {
                            s_lastRemoteManifestJson = request.downloadHandler.text;
                            ProcessRemoteManifest(s_lastRemoteManifestJson);
                        }
                    }
                    catch
                    {
                        // Non-fatal background check
                    }
                    finally
                    {
                        request.Dispose();
                        s_isCheckingRemoteUpdate = false;
                    }
                };
            }
            catch
            {
                s_isCheckingRemoteUpdate = false;
            }
        }

        public static void ProcessRemoteManifest(string jsonText, FxvCliVersion? localCliOverride = null)
        {
            string prevVer = AvailableUpdateVersion;
            string prevRange = AvailableUpdateCliRange;

            if (string.IsNullOrWhiteSpace(jsonText))
            {
                s_lastRemoteManifestJson = null;
                AvailableUpdateVersion = null;
                AvailableUpdateCliRange = null;
                if (prevVer != null) NotifyUpdateChanged();
                return;
            }

            s_lastRemoteManifestJson = jsonText;

            try
            {
                var manifest = JsonConvert.DeserializeObject<RemoteReleaseManifest>(jsonText);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.PluginVersion))
                {
                    AvailableUpdateVersion = null;
                    AvailableUpdateCliRange = null;
                    if (prevVer != null) NotifyUpdateChanged();
                    return;
                }

                if (!FxvCliVersion.TryParse(manifest.PluginVersion, out var remoteVer))
                {
                    AvailableUpdateVersion = null;
                    AvailableUpdateCliRange = null;
                    if (prevVer != null) NotifyUpdateChanged();
                    return;
                }

                string currentVerStr = PluginVersion;
                if (!FxvCliVersion.TryParse(currentVerStr, out var currentVer))
                {
                    AvailableUpdateVersion = null;
                    AvailableUpdateCliRange = null;
                    if (prevVer != null) NotifyUpdateChanged();
                    return;
                }

                if (remoteVer <= currentVer)
                {
                    AvailableUpdateVersion = null;
                    AvailableUpdateCliRange = null;
                    if (prevVer != null) NotifyUpdateChanged();
                    return;
                }

                if (manifest.CliCompatibility == null ||
                    !FxvCliVersion.TryParse(manifest.CliCompatibility.Min, out var minCli) ||
                    !FxvCliVersion.TryParse(manifest.CliCompatibility.Max, out var maxCli))
                {
                    AvailableUpdateVersion = null;
                    AvailableUpdateCliRange = null;
                    if (prevVer != null) NotifyUpdateChanged();
                    return;
                }

                FxvCliVersion? cliVer = localCliOverride ?? s_cachedVersion;
                if (!cliVer.HasValue || cliVer.Value < minCli || cliVer.Value >= maxCli)
                {
                    AvailableUpdateVersion = null;
                    AvailableUpdateCliRange = null;
                    if (prevVer != null) NotifyUpdateChanged();
                    return;
                }

                AvailableUpdateVersion = manifest.PluginVersion;
                AvailableUpdateCliRange = $">= {minCli}, < {maxCli}";

                if (prevVer != AvailableUpdateVersion || prevRange != AvailableUpdateCliRange)
                {
                    NotifyUpdateChanged();
                }
            }
            catch
            {
                AvailableUpdateVersion = null;
                AvailableUpdateCliRange = null;
                if (prevVer != null) NotifyUpdateChanged();
            }
        }

        public static bool CheckAndCacheVersion(string versionStr, out string errorMessage)
        {
            bool ok = CheckVersion(versionStr, out errorMessage);
            s_isVersionCompatible = ok;
            s_lastVersionString = versionStr;
            s_lastErrorMessage = errorMessage;
            if (FxvCliVersion.TryParse(versionStr, out var parsed))
            {
                s_cachedVersion = parsed;
            }
            else
            {
                s_cachedVersion = null;
            }

            if (!string.IsNullOrEmpty(s_lastRemoteManifestJson))
            {
                ProcessRemoteManifest(s_lastRemoteManifestJson);
            }

            return ok;
        }

        public static bool CheckVersion(string versionStr, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(versionStr))
            {
                errorMessage = "FlexVault CLI version string is missing or empty.";
                return false;
            }

            if (!FxvCliVersion.TryParse(versionStr, out var cliVersion))
            {
                errorMessage = $"Invalid FlexVault CLI version string '{versionStr}'.";
                return false;
            }

            if (cliVersion < MinVersion)
            {
                errorMessage = $"Incompatible FlexVault CLI version '{versionStr}'. This plugin requires fxv >= {MinVersion}, < {MaxVersion}. Please upgrade your fxv CLI executable to a compatible version (e.g. run 'fxv update' or download the latest release from https://fxv.dev).";
                return false;
            }

            if (cliVersion >= MaxVersion)
            {
                errorMessage = $"Incompatible FlexVault CLI version '{versionStr}'. This plugin requires fxv >= {MinVersion}, < {MaxVersion}. Please update the FlexVault Unity plugin to match your CLI version, or configure a compatible CLI path in Project Settings > Version Control > FlexVault.";
                return false;
            }

            return true;
        }

        public const string DefaultPluginVersion = "unknown";

        public static string PluginVersion
        {
            get
            {
                if (!string.IsNullOrEmpty(s_pluginVersion))
                {
                    return s_pluginVersion;
                }

                s_pluginVersion = ResolvePluginVersion();
                return s_pluginVersion;
            }
            set => s_pluginVersion = value;
        }

        private static string s_pluginVersion;

        public static string ResolvePluginVersion()
        {
            try
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FlexVaultVersionGuard).Assembly);
                if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.version))
                {
                    return packageInfo.version;
                }
            }
            catch
            {
                // In non-editor or standalone test runner environments, FindForAssembly may fail
            }

            try
            {
                // Assembly.Location legitimately returns "" for assemblies Unity loads from a
                // memory stream (triggers analyzer warning UAC0007); GetLoadedAssemblyPath() isn't
                // available in this Unity version's API surface, so fall back to Location and treat
                // an empty result the same as any other resolution failure below.
#pragma warning disable UAC0007
                string asmPath = typeof(FlexVaultVersionGuard).Assembly.Location;
#pragma warning restore UAC0007
                if (!string.IsNullOrEmpty(asmPath))
                {
                    string dir = Path.GetDirectoryName(asmPath);
                    while (!string.IsNullOrEmpty(dir))
                    {
                        string pkgJson = Path.Combine(dir, "package.json");
                        if (File.Exists(pkgJson))
                        {
                            string v = TryReadVersionFromPackageJson(pkgJson);
                            if (!string.IsNullOrEmpty(v))
                            {
                                return v;
                            }
                        }
                        DirectoryInfo parent = Directory.GetParent(dir);
                        if (parent == null) break;
                        dir = parent.FullName;
                    }
                }
            }
            catch
            {
                // Ignore filesystem search errors
            }

            try
            {
                if (!string.IsNullOrEmpty(UnityEngine.Application.dataPath))
                {
                    string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
                    string[] candidates = {
                        Path.Combine(projectRoot, "Packages", "com.flexvault.vcs", "package.json"),
                        Path.Combine(projectRoot, "package.json")
                    };
                    foreach (var candidate in candidates)
                    {
                        if (File.Exists(candidate))
                        {
                            string v = TryReadVersionFromPackageJson(candidate);
                            if (!string.IsNullOrEmpty(v))
                            {
                                return v;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return DefaultPluginVersion;
        }

        public static string TryReadVersionFromPackageJson(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path);
                return ParseVersionFromPackageJsonContent(text);
            }
            catch
            {
                return null;
            }
        }

        public static string ParseVersionFromPackageJsonContent(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                return null;
            }

            var match = Regex.Match(jsonContent, @"""version""\s*:\s*""([^""]+)""");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
    }
}
