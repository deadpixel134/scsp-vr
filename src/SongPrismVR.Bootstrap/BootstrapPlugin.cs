using System.Diagnostics;
using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SongPrismVR.Bootstrap;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("imasscprism.exe")]
public sealed class BootstrapPlugin : BasePlugin
{
    public const string PluginGuid = "io.github.songprismvr.bootstrap";
    public const string PluginName = "SongPrismVR Bootstrap";
    public const string PluginVersion = "0.1.1-preview.1";

    public override void Load()
    {
        string outputDirectory = Path.Combine(Paths.ConfigPath, "SongPrismVR");
        Directory.CreateDirectory(outputDirectory);
        string healthPath = Path.Combine(outputDirectory, "bootstrap-health.json");
        string localifyPath = Path.Combine(Paths.GameRootPath, "version.dll");

        var health = new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            status = "loaded",
            pluginVersion = PluginVersion,
            processName = Process.GetCurrentProcess().ProcessName,
            localifyProxyPresent = File.Exists(localifyPath),
            interopDirectoryPresent = Directory.Exists(Path.Combine(Paths.BepInExRootPath, "interop"))
        };

        File.WriteAllText(
            healthPath,
            JsonSerializer.Serialize(health, new JsonSerializerOptions { WriteIndented = true }));
        Log.LogInfo($"{PluginName} {PluginVersion} loaded; coexistence health written to {healthPath}");
    }
}
