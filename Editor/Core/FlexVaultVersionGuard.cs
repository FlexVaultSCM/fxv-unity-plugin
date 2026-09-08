using System;

namespace FlexVault.VCS.Editor.Core
{
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
        // MinVersion is 0.5.0 because 'fxv cat' required for diff/history was introduced in v0.5.0.
        // MaxVersion is 0.9.0 to support releases through v0.8.x.
        public static readonly FxvCliVersion MinVersion = new FxvCliVersion(0, 5, 0); // >= 0.5.0
        public static readonly FxvCliVersion MaxVersion = new FxvCliVersion(0, 9, 0); // < 0.9.0

        public static bool? IsVersionCompatible => s_isVersionCompatible;
        public static string LastVersionString => s_lastVersionString;
        public static string LastErrorMessage => s_lastErrorMessage;

        private static bool? s_isVersionCompatible;
        private static string s_lastVersionString;
        private static string s_lastErrorMessage;

        public static void ResetCachedVersion()
        {
            s_isVersionCompatible = null;
            s_lastVersionString = null;
            s_lastErrorMessage = null;
        }

        public static bool CheckAndCacheVersion(string versionStr, out string errorMessage)
        {
            bool ok = CheckVersion(versionStr, out errorMessage);
            s_isVersionCompatible = ok;
            s_lastVersionString = versionStr;
            s_lastErrorMessage = errorMessage;
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

            if (cliVersion < MinVersion || cliVersion >= MaxVersion)
            {
                errorMessage = $"Incompatible FlexVault CLI version '{versionStr}'. This plugin supports fxv >= {MinVersion}, < {MaxVersion}.";
                return false;
            }

            return true;
        }
    }
}
