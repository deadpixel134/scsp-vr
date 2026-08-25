using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SongPrismVR.Core;

namespace Doorstop;

internal static class VrSettingsRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private static readonly object Sync = new();
    private static VrSettings _current = VrSettings.CreateApprovedDefaults();
    private static bool _initialized;

    internal static VrSettings Current
    {
        get
        {
            lock (Sync)
            {
                return _current;
            }
        }
    }

    internal static VrSettings Initialize(string logPath)
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return _current;
            }

            _current = Load(logPath, out string source, out IReadOnlyList<string> issues);
            _initialized = true;
            RuntimeProbe.Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = issues.Count == 0 ? "vr-settings-loaded" : "vr-settings-fallback",
                BootstrapVersion = RuntimeProbe.BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Reason =
                    $"source={source};schema={_current.SchemaVersion};enabled={_current.Runtime.Enabled};" +
                    $"panelHand={_current.Panel.PanelHand};pointerHand={_current.Panel.PointerHand};" +
                    $"viewerFacing={_current.Panel.ViewerFacing};eyeScale={_current.Render.EyeRenderScale:R};" +
                    $"liveSixDof={_current.Tracking.LiveSixDofEnabled};" +
                    $"locomotion={_current.Tracking.LocomotionEnabled};" +
                    $"locomotionHand={_current.Tracking.LocomotionHand};" +
                    $"locomotionSpeed={_current.Tracking.LocomotionSpeed:R};" +
                    $"viewTurnMode={_current.Tracking.ViewTurnMode};" +
                    $"viewTurnSpeed={_current.Tracking.ViewTurnSpeed:R};" +
                    $"viewSnapAngle={_current.Tracking.ViewSnapAngleDegrees};" +
                    $"vfxMode={_current.Render.VisualEffectMode};" +
                    $"manualPost={_current.Render.ManualVisualEffects.PostProcessingEnabled};" +
                    $"manualBloom={_current.Render.ManualVisualEffects.VlBloomEnabled};" +
                    $"manualBloomIntensity={_current.Render.ManualVisualEffects.VlBloomIntensityScale:R};" +
                    $"manualBloomDiffusion={_current.Render.ManualVisualEffects.VlBloomDiffusion};" +
                    $"issues={(issues.Count == 0 ? "none" : string.Join(',', issues))}"
            });
            return _current;
        }
    }

    internal static string GetSettingsPath(string logPath)
    {
        string logDirectory = Path.GetDirectoryName(logPath)
            ?? throw new InvalidOperationException("Runtime log directory is unavailable.");
        string vrmodDirectory = Directory.GetParent(logDirectory)?.FullName
            ?? throw new InvalidOperationException("VR mod directory is unavailable.");
        return Path.Combine(vrmodDirectory, "config", "settings.json");
    }

    private static VrSettings Load(
        string logPath,
        out string source,
        out IReadOnlyList<string> issues)
    {
        string settingsPath = GetSettingsPath(logPath);
        if (File.Exists(settingsPath))
        {
            try
            {
                VrSettings? parsed = JsonSerializer.Deserialize<VrSettings>(
                    File.ReadAllText(settingsPath),
                    JsonOptions);
                VrSettingsValidationResult validation = VrSettingsValidator.Validate(parsed);
                source = "settings.json";
                issues = validation.Issues;
                return validation.Settings;
            }
            catch (Exception exception)
            {
                source = "settings.json-invalid";
                issues = new[] { $"json:{exception.GetType().Name}" };
                return VrSettings.CreateApprovedDefaults();
            }
        }

        VrSettings legacy = VrSettings.CreateApprovedDefaults();
        List<string> legacyIssues = new();
        string configDirectory = Path.GetDirectoryName(settingsPath)!;
        string scalePath = Path.Combine(configDirectory, "render-resolution-scale.txt");
        if (File.Exists(scalePath))
        {
            string value = File.ReadAllText(scalePath).Trim();
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
            {
                legacy.Render.EyeRenderScale = scale;
            }
            else
            {
                legacyIssues.Add("legacy-eye-scale:invalid");
            }
        }

        string visualEffectPath = Path.Combine(configDirectory, "visual-effect-mode.txt");
        if (File.Exists(visualEffectPath))
        {
            legacy.Render.VisualEffectMode = File.ReadAllText(visualEffectPath).Trim();
        }

        VrSettingsValidationResult legacyValidation = VrSettingsValidator.Validate(legacy);
        legacyIssues.AddRange(legacyValidation.Issues);
        source = "legacy-text-or-defaults";
        issues = legacyIssues;
        return legacyValidation.Settings;
    }
}
