using System.Globalization;
using System.Security.Cryptography;

namespace SongPrismVR.Management;

public static class ReleaseUpdatePolicy
{
    public static bool IsNewer(string currentVersion, string releaseTag) => CompareVersions(releaseTag, currentVersion) > 0;
    public static int CompareVersions(string left, string right) => ParseSemanticVersion(left).CompareTo(ParseSemanticVersion(right));
    public static string NormalizeVersion(string value) => ParseSemanticVersion(value).Normalized;

    public static Version ParseVersion(string value)
    {
        SemanticVersion parsed = ParseSemanticVersion(value);
        return new Version(parsed.Major, parsed.Minor, parsed.Patch);
    }

    public static string ParseSha256(string value, string assetName)
    {
        foreach (string rawLine in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return RequireSha256(line[7..].Trim());
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return RequireSha256(parts[0]);
            if (parts.Length >= 2 && string.Equals(parts[^1].TrimStart('*'), assetName, StringComparison.Ordinal)) return RequireSha256(parts[0]);
        }
        throw new InvalidDataException("The release checksum does not name the expected asset.");
    }

    public static string FileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static SemanticVersion ParseSemanticVersion(string value)
    {
        string normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        int buildMetadata = normalized.IndexOf('+');
        if (buildMetadata >= 0)
        {
            string[] build = normalized[(buildMetadata + 1)..].Split('.');
            if (build.Any(identifier => !IsValidIdentifier(identifier)))
                throw new InvalidDataException($"Invalid semantic release version: {value}");
            normalized = normalized[..buildMetadata];
        }

        string core = normalized;
        string[] prerelease = [];
        int prereleaseSeparator = normalized.IndexOf('-');
        if (prereleaseSeparator >= 0)
        {
            core = normalized[..prereleaseSeparator];
            prerelease = normalized[(prereleaseSeparator + 1)..].Split('.');
        }

        string[] coreParts = core.Split('.');
        if (coreParts.Length != 3 || !TryParseIdentifier(coreParts[0], out int major) ||
            !TryParseIdentifier(coreParts[1], out int minor) || !TryParseIdentifier(coreParts[2], out int patch) ||
            prerelease.Any(identifier => !IsValidPrereleaseIdentifier(identifier)))
        {
            throw new InvalidDataException($"Invalid semantic release version: {value}");
        }
        return new SemanticVersion(major, minor, patch, prerelease);
    }

    private static bool TryParseIdentifier(string value, out int parsed)
    {
        parsed = 0;
        return value.Length != 0 && (value.Length == 1 || value[0] != '0') &&
            value.All(char.IsAsciiDigit) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool IsValidPrereleaseIdentifier(string value)
    {
        if (!IsValidIdentifier(value)) return false;
        return !value.All(char.IsAsciiDigit) || value.Length == 1 || value[0] != '0';
    }

    private static bool IsValidIdentifier(string value) =>
        value.Length != 0 && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static string RequireSha256(string value)
    {
        string normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The release SHA-256 value is invalid.");
        return normalized;
    }

    private sealed record SemanticVersion(int Major, int Minor, int Patch, IReadOnlyList<string> Prerelease) : IComparable<SemanticVersion>
    {
        public string Normalized => $"{Major}.{Minor}.{Patch}" + (Prerelease.Count == 0 ? string.Empty : $"-{string.Join('.', Prerelease)}");

        public int CompareTo(SemanticVersion? other)
        {
            if (other is null) return 1;
            int core = Major.CompareTo(other.Major);
            if (core == 0) core = Minor.CompareTo(other.Minor);
            if (core == 0) core = Patch.CompareTo(other.Patch);
            if (core != 0) return core;
            if (Prerelease.Count == 0) return other.Prerelease.Count == 0 ? 0 : 1;
            if (other.Prerelease.Count == 0) return -1;

            int shared = Math.Min(Prerelease.Count, other.Prerelease.Count);
            for (int index = 0; index < shared; index++)
            {
                string left = Prerelease[index];
                string right = other.Prerelease[index];
                bool leftNumeric = left.All(char.IsAsciiDigit);
                bool rightNumeric = right.All(char.IsAsciiDigit);
                int compared = leftNumeric && rightNumeric ? CompareNumericIdentifiers(left, right) :
                    leftNumeric ? -1 : rightNumeric ? 1 : string.CompareOrdinal(left, right);
                if (compared != 0) return compared;
            }
            return Prerelease.Count.CompareTo(other.Prerelease.Count);
        }

        private static int CompareNumericIdentifiers(string left, string right)
        {
            int length = left.Length.CompareTo(right.Length);
            return length != 0 ? length : string.CompareOrdinal(left, right);
        }
    }
}
