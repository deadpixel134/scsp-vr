using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using SongPrismVR.Core;

namespace SongPrismVR.Configurator;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string FindInitialGameRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int depth = 0; depth < 5 && current is not null; depth++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "imasscprism.exe")))
            {
                return current.FullName;
            }
        }
        return Environment.CurrentDirectory;
    }

    public static VrSettings LoadFromGameRoot(string gameRoot)
    {
        string path = SettingsPath(gameRoot);
        if (!File.Exists(path))
        {
            return VrSettings.CreateApprovedDefaults();
        }

        VrSettings? parsed = JsonSerializer.Deserialize<VrSettings>(
            File.ReadAllText(path),
            JsonOptions);
        return VrSettingsValidator.Validate(parsed).Settings;
    }

    public static VrSettings LoadFile(string path)
    {
        VrSettings? parsed = JsonSerializer.Deserialize<VrSettings>(
            File.ReadAllText(path),
            JsonOptions);
        VrSettingsValidationResult result = VrSettingsValidator.Validate(parsed);
        if (result.UsedFallback)
        {
            throw new InvalidDataException(
                UiText.Format("InvalidSettingsFile", string.Join(", ", result.Issues)));
        }
        return result.Settings;
    }

    public static void SaveToGameRoot(string gameRoot, VrSettings settings)
    {
        EnsureGameStopped();
        if (!File.Exists(Path.Combine(gameRoot, "imasscprism.exe")))
        {
            throw new DirectoryNotFoundException(UiText.Get("GameExeMissing"));
        }
        SaveAtomic(SettingsPath(gameRoot), settings, createBackup: true);
    }

    public static void Export(string path, VrSettings settings) =>
        SaveAtomic(path, settings, createBackup: false);

    private static string SettingsPath(string gameRoot) =>
        Path.Combine(Path.GetFullPath(gameRoot), "vrmod", "config", "settings.json");

    private static void EnsureGameStopped()
    {
        using Process current = Process.GetCurrentProcess();
        if (Process.GetProcessesByName("imasscprism").Any(process =>
            process.Id != current.Id))
        {
            throw new InvalidOperationException(
                UiText.Get("GameRunning"));
        }
    }

    private static void SaveAtomic(string path, VrSettings settings, bool createBackup)
    {
        VrSettingsValidationResult validation = VrSettingsValidator.Validate(settings);
        if (validation.UsedFallback)
        {
            throw new InvalidDataException(
                UiText.Format("InvalidSettingsToSave", string.Join(", ", validation.Issues)));
        }

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ??
            throw new InvalidOperationException(UiText.Get("SettingsPathNoParent"));
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        string backup = fullPath + ".bak";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(validation.Settings, JsonOptions) + Environment.NewLine);
            using (FileStream stream = new(
                temporary,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
                File.Replace(
                    temporary,
                    fullPath,
                    createBackup ? backup : null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, fullPath);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
