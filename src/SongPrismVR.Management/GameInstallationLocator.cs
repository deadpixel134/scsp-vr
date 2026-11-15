using System.Text.Json;

namespace SongPrismVR.Management;

public static class GameInstallationLocator
{
    private const int MaximumConfigBytes = 4 * 1024 * 1024;

    public static string? FindGameRoot(
        string applicationDirectory,
        string workingDirectory,
        IEnumerable<string> dmmConfigurationPaths)
    {
        // An installer placed in the game tree has an explicit local context.
        DirectoryInfo? current = new(applicationDirectory);
        for (int depth = 0; depth < 7 && current is not null; depth++, current = current.Parent)
        {
            string? gameRoot = ValidateGameRoot(current.FullName);
            if (gameRoot is not null)
            {
                return gameRoot;
            }
        }

        string? workingGameRoot = ValidateGameRoot(workingDirectory);
        if (workingGameRoot is not null)
        {
            return workingGameRoot;
        }

        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
        foreach (string configurationPath in dmmConfigurationPaths)
        {
            ReadDmmGameRoots(configurationPath, candidates);
        }

        // Do not silently pick between separate, valid installations.
        return candidates.Count == 1 ? candidates.Single() : null;
    }

    public static IEnumerable<string> DmmConfigurationPaths()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(roaming))
        {
            yield return Path.Combine(roaming, "dmmgameplayer5", "dmmgame.cnf");
            yield return Path.Combine(roaming, "DMMGamePlayer", "dmmgame.cnf");
        }
        if (!string.IsNullOrEmpty(local))
        {
            yield return Path.Combine(local, "DMMGamePlayer", "dmmgame.cnf");
        }
    }

    private static void ReadDmmGameRoots(string configurationPath, HashSet<string> candidates)
    {
        try
        {
            using FileStream stream = new(
                configurationPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumConfigBytes)
            {
                return;
            }
            using StreamReader reader = new(stream);
            using JsonDocument document = JsonDocument.Parse(reader.ReadToEnd());
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("contents", out JsonElement contents) ||
                contents.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement content in contents.EnumerateArray())
            {
                if (content.ValueKind != JsonValueKind.Object ||
                    !content.TryGetProperty("detail", out JsonElement detail) ||
                    detail.ValueKind != JsonValueKind.Object ||
                    (detail.TryGetProperty("installed", out JsonElement installed) &&
                        installed.ValueKind != JsonValueKind.True) ||
                    !detail.TryGetProperty("path", out JsonElement path) ||
                    path.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                // Match the actual game files, not a guessed DMM product ID.
                string? gameRoot = ValidateGameRoot(path.GetString());
                if (gameRoot is not null)
                {
                    candidates.Add(gameRoot);
                }
            }
        }
        catch (Exception exception) when (IsDiscoveryFailure(exception) || exception is JsonException)
        {
            // Missing, locked, or partially written launcher data is optional.
            // Never log its contents: launcher files can contain private data.
        }
    }

    private static string? ValidateGameRoot(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                return null;
            }
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return InstallationEngine.IsGameRoot(fullPath) ? fullPath : null;
        }
        catch (Exception exception) when (IsDiscoveryFailure(exception))
        {
            return null;
        }
    }

    private static bool IsDiscoveryFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException;
}
