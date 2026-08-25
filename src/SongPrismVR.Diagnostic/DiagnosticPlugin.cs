using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SongPrismVR.Diagnostic;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("imasscprism.exe")]
public sealed class DiagnosticPlugin : BasePlugin
{
    public const string PluginGuid = "io.github.songprismvr.diagnostic";
    public const string PluginName = "SongPrismVR Diagnostic";
    public const string PluginVersion = "0.1.1-preview.1";

    public override void Load()
    {
        string outputDirectory = Path.Combine(Paths.ConfigPath, "SongPrismVR");
        Directory.CreateDirectory(outputDirectory);

        DiagnosticBehaviour behaviour = AddComponent<DiagnosticBehaviour>();
        behaviour.Initialize(Log, Path.Combine(outputDirectory, "diagnostics.jsonl"));
        Log.LogInfo($"{PluginName} {PluginVersion} loaded. Diagnostics: {outputDirectory}");
    }
}
