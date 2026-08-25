namespace SongPrismVR.Core;

public enum VrHand
{
    Left,
    Right
}

public enum VrViewTurnMode
{
    Smooth,
    Snap
}

public enum FaceButtonBinding
{
    Primary,
    Secondary
}

public enum PanelToggleBinding
{
    Grip,
    PrimaryFace,
    SecondaryFace
}

public enum SpatialScaleMode
{
    Auto,
    Manual
}

public static class VrVisualEffectModes
{
    public const string Approved = "vr-safe-standard";
    public const string AllOn = "all-on";
    public const string AllOff = "all-off";
    public const string Manual = "manual";
}

public sealed class VrSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public VrRuntimeSettings Runtime { get; set; } = new();

    public VrRenderSettings Render { get; set; } = new();

    public VrTrackingSettings Tracking { get; set; } = new();

    public VrSpatialSettings Spatial { get; set; } = new();

    public VrPanelSettings Panel { get; set; } = new();

    public VrInputSettings Input { get; set; } = new();

    public static VrSettings CreateApprovedDefaults() => new();
}

public sealed class VrRuntimeSettings
{
    public bool Enabled { get; set; } = true;
}

public sealed class VrRenderSettings
{
    public float EyeRenderScale { get; set; } = 0.65f;

    public float WorldEyeOffsetScale { get; set; } = 0.275f;

    public string VisualEffectMode { get; set; } = VrVisualEffectModes.AllOff;

    public VrManualVisualEffectSettings ManualVisualEffects { get; set; } = new();
}

public sealed class VrTrackingSettings
{
    public bool LiveSixDofEnabled { get; set; } = true;

    public bool LocomotionEnabled { get; set; } = true;

    public VrHand LocomotionHand { get; set; } = VrHand.Right;

    public float LocomotionSpeed { get; set; } = 1.95f;

    public VrViewTurnMode ViewTurnMode { get; set; } = VrViewTurnMode.Snap;

    public float ViewTurnSpeed { get; set; } = 90f;

    public int ViewSnapAngleDegrees { get; set; } = 30;
}

public static class VrLivePositionPolicy
{
    public static bool SynchronizeToGameCamera(bool liveSixDofEnabled) =>
        !liveSixDofEnabled;

    public static bool EnableLiveSixDof(bool synchronizeToGameCamera) =>
        !synchronizeToGameCamera;
}

public sealed class VrSpatialSettings
{
    public VrSpatialScaleProfile Live { get; set; } = new();

    public VrSpatialScaleProfile NonLive { get; set; } = new();
}

public sealed class VrSpatialScaleProfile
{
    public float PerceivedCharacterScale { get; set; } = 1f;

    public SpatialScaleMode EyeOffsetMode { get; set; } = SpatialScaleMode.Auto;

    public float EyeOffsetMultiplier { get; set; } = 1f;

    public SpatialScaleMode HeadTranslationMode { get; set; } = SpatialScaleMode.Auto;

    public float HeadTranslationMultiplier { get; set; } = 1f;

    public SpatialScaleMode LocomotionMode { get; set; } = SpatialScaleMode.Auto;

    public float LocomotionMultiplier { get; set; } = 1f;
}

public sealed class VrManualVisualEffectSettings
{
    public bool PostProcessingEnabled { get; set; } = true;

    public bool VlBloomEnabled { get; set; } = true;

    public float VlBloomIntensityScale { get; set; } = 1.40f;

    public int VlBloomDiffusion { get; set; } = 1;

    public bool VlDepthOfFieldEnabled { get; set; }

    public bool VlTextureBlurEnabled { get; set; }

    public bool VlStarStreakEnabled { get; set; } = true;

    public bool VlFlareEnabled { get; set; } = true;
}

public sealed class VrPanelSettings
{
    public VrHand PanelHand { get; set; } = VrHand.Left;

    public VrHand PointerHand { get; set; } = VrHand.Right;

    public bool StartEnabled { get; set; }

    public bool ViewerFacing { get; set; } = true;

    public float OffsetX { get; set; }

    public float OffsetY { get; set; } = 0.10f;

    public float OffsetZ { get; set; }

    public float MaximumWidth { get; set; } = 0.42f;

    public float MaximumHeight { get; set; } = 0.42f;

    public float RotationPitch { get; set; }

    public float RotationYaw { get; set; }

    public float RotationRoll { get; set; }

    public int VisibilityHysteresisMilliseconds { get; set; } = 100;

    public PanelToggleBinding ToggleBinding { get; set; } = PanelToggleBinding.Grip;
}

public sealed class VrInputSettings
{
    public FaceButtonBinding PrimaryClickButton { get; set; } = FaceButtonBinding.Primary;

    public FaceButtonBinding BackButton { get; set; } = FaceButtonBinding.Secondary;

    public bool TriggerClickEnabled { get; set; } = true;

    public bool ThumbstickScrollEnabled { get; set; }

    public float ScrollSensitivity { get; set; } = 1f;

    public bool RequireGameFocus { get; set; } = true;
}

public sealed class VrSettingsValidationResult
{
    public VrSettings Settings { get; set; } = VrSettings.CreateApprovedDefaults();

    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();

    public bool UsedFallback => Issues.Count != 0;
}

public static class VrSettingsValidator
{
    private const float SafeEyeRenderScale = 0.75f;

    public static VrSettingsValidationResult Validate(VrSettings? source)
    {
        List<string> issues = new();
        if (source is null)
        {
            issues.Add("settings-null");
            return Result(VrSettings.CreateApprovedDefaults(), issues);
        }

        if (source.SchemaVersion != VrSettings.CurrentSchemaVersion)
        {
            issues.Add($"unsupported-schema:{source.SchemaVersion}");
            return Result(VrSettings.CreateApprovedDefaults(), issues);
        }

        VrSettings defaults = VrSettings.CreateApprovedDefaults();
        VrRuntimeSettings runtime = source.Runtime ?? defaults.Runtime;
        VrRenderSettings render = source.Render ?? defaults.Render;
        VrTrackingSettings tracking = source.Tracking ?? defaults.Tracking;
        VrSpatialSettings spatial = source.Spatial ?? defaults.Spatial;
        VrManualVisualEffectSettings manualVisualEffects =
            render.ManualVisualEffects ?? defaults.Render.ManualVisualEffects;
        VrPanelSettings panel = source.Panel ?? defaults.Panel;
        VrInputSettings input = source.Input ?? defaults.Input;

        VrSettings validated = new()
        {
            SchemaVersion = VrSettings.CurrentSchemaVersion,
            Runtime = new VrRuntimeSettings { Enabled = runtime.Enabled },
            Render = new VrRenderSettings
            {
                EyeRenderScale = ValidateRange(
                    render.EyeRenderScale,
                    0.50f,
                    2.00f,
                    SafeEyeRenderScale,
                    "render.eyeRenderScale",
                    issues),
                WorldEyeOffsetScale = ValidateRange(
                    render.WorldEyeOffsetScale,
                    0f,
                    0.50f,
                    defaults.Render.WorldEyeOffsetScale,
                    "render.worldEyeOffsetScale",
                    issues),
                VisualEffectMode = ValidateVisualEffectMode(
                    render.VisualEffectMode,
                    defaults.Render.VisualEffectMode,
                    issues),
                ManualVisualEffects = new VrManualVisualEffectSettings
                {
                    PostProcessingEnabled = manualVisualEffects.PostProcessingEnabled,
                    VlBloomEnabled = manualVisualEffects.VlBloomEnabled,
                    VlBloomIntensityScale = ValidateRange(
                        manualVisualEffects.VlBloomIntensityScale,
                        0f,
                        3f,
                        defaults.Render.ManualVisualEffects.VlBloomIntensityScale,
                        "render.manualVisualEffects.vlBloomIntensityScale",
                        issues),
                    VlBloomDiffusion = ValidateRange(
                        manualVisualEffects.VlBloomDiffusion,
                        1,
                        10,
                        defaults.Render.ManualVisualEffects.VlBloomDiffusion,
                        "render.manualVisualEffects.vlBloomDiffusion",
                        issues),
                    VlDepthOfFieldEnabled = manualVisualEffects.VlDepthOfFieldEnabled,
                    VlTextureBlurEnabled = manualVisualEffects.VlTextureBlurEnabled,
                    VlStarStreakEnabled = manualVisualEffects.VlStarStreakEnabled,
                    VlFlareEnabled = manualVisualEffects.VlFlareEnabled
                }
            },
            Tracking = new VrTrackingSettings
            {
                LiveSixDofEnabled = tracking.LiveSixDofEnabled,
                LocomotionEnabled = tracking.LocomotionEnabled,
                LocomotionHand = ValidateEnum(
                    tracking.LocomotionHand,
                    defaults.Tracking.LocomotionHand,
                    "tracking.locomotionHand",
                    issues),
                LocomotionSpeed = ValidateRange(
                    tracking.LocomotionSpeed,
                    0.10f,
                    5f,
                    defaults.Tracking.LocomotionSpeed,
                    "tracking.locomotionSpeed",
                    issues),
                ViewTurnMode = ValidateEnum(
                    tracking.ViewTurnMode,
                    defaults.Tracking.ViewTurnMode,
                    "tracking.viewTurnMode",
                    issues),
                ViewTurnSpeed = ValidateRange(
                    tracking.ViewTurnSpeed,
                    15f,
                    180f,
                    defaults.Tracking.ViewTurnSpeed,
                    "tracking.viewTurnSpeed",
                    issues),
                ViewSnapAngleDegrees = ValidateSnapAngle(
                    tracking.ViewSnapAngleDegrees,
                    defaults.Tracking.ViewSnapAngleDegrees,
                    issues)
            },
            Spatial = new VrSpatialSettings
            {
                Live = ValidateSpatialProfile(
                    spatial.Live,
                    defaults.Spatial.Live,
                    "spatial.live",
                    issues),
                NonLive = ValidateSpatialProfile(
                    spatial.NonLive,
                    defaults.Spatial.NonLive,
                    "spatial.nonLive",
                    issues)
            },
            Panel = new VrPanelSettings
            {
                PanelHand = ValidateEnum(panel.PanelHand, defaults.Panel.PanelHand, "panel.panelHand", issues),
                PointerHand = ValidateEnum(panel.PointerHand, defaults.Panel.PointerHand, "panel.pointerHand", issues),
                StartEnabled = panel.StartEnabled,
                ViewerFacing = panel.ViewerFacing,
                OffsetX = ValidateRange(panel.OffsetX, -0.50f, 0.50f, defaults.Panel.OffsetX, "panel.offsetX", issues),
                OffsetY = ValidateRange(panel.OffsetY, -0.50f, 0.50f, defaults.Panel.OffsetY, "panel.offsetY", issues),
                OffsetZ = ValidateRange(panel.OffsetZ, -0.50f, 0.50f, defaults.Panel.OffsetZ, "panel.offsetZ", issues),
                MaximumWidth = ValidateRange(panel.MaximumWidth, 0.10f, 1.00f, defaults.Panel.MaximumWidth, "panel.maximumWidth", issues),
                MaximumHeight = ValidateRange(panel.MaximumHeight, 0.10f, 1.00f, defaults.Panel.MaximumHeight, "panel.maximumHeight", issues),
                RotationPitch = ValidateRange(panel.RotationPitch, -180f, 180f, defaults.Panel.RotationPitch, "panel.rotationPitch", issues),
                RotationYaw = ValidateRange(panel.RotationYaw, -180f, 180f, defaults.Panel.RotationYaw, "panel.rotationYaw", issues),
                RotationRoll = ValidateRange(panel.RotationRoll, -180f, 180f, defaults.Panel.RotationRoll, "panel.rotationRoll", issues),
                VisibilityHysteresisMilliseconds = ValidateRange(
                    panel.VisibilityHysteresisMilliseconds,
                    0,
                    500,
                    defaults.Panel.VisibilityHysteresisMilliseconds,
                    "panel.visibilityHysteresisMilliseconds",
                    issues),
                ToggleBinding = ValidateEnum(panel.ToggleBinding, defaults.Panel.ToggleBinding, "panel.toggleBinding", issues)
            },
            Input = new VrInputSettings
            {
                PrimaryClickButton = ValidateEnum(input.PrimaryClickButton, defaults.Input.PrimaryClickButton, "input.primaryClickButton", issues),
                BackButton = ValidateEnum(input.BackButton, defaults.Input.BackButton, "input.backButton", issues),
                TriggerClickEnabled = input.TriggerClickEnabled,
                ThumbstickScrollEnabled = false,
                ScrollSensitivity = ValidateRange(input.ScrollSensitivity, 0.10f, 5f, defaults.Input.ScrollSensitivity, "input.scrollSensitivity", issues),
                RequireGameFocus = input.RequireGameFocus
            }
        };

        if (validated.Panel.PanelHand == validated.Panel.PointerHand)
        {
            validated.Panel.PointerHand = validated.Panel.PanelHand == VrHand.Left
                ? VrHand.Right
                : VrHand.Left;
            issues.Add("panel.pointerHand:must-differ-from-panelHand");
        }

        if (validated.Input.PrimaryClickButton == validated.Input.BackButton)
        {
            validated.Input.PrimaryClickButton = defaults.Input.PrimaryClickButton;
            validated.Input.BackButton = defaults.Input.BackButton;
            issues.Add("input.face-buttons:must-differ");
        }

        return Result(validated, issues);
    }

    private static VrSettingsValidationResult Result(VrSettings settings, List<string> issues) =>
        new() { Settings = settings, Issues = issues };

    private static VrSpatialScaleProfile ValidateSpatialProfile(
        VrSpatialScaleProfile? source,
        VrSpatialScaleProfile defaults,
        string prefix,
        List<string> issues)
    {
        source ??= defaults;
        return new VrSpatialScaleProfile
        {
            PerceivedCharacterScale = ValidateRange(
                source.PerceivedCharacterScale,
                0.10f,
                4f,
                defaults.PerceivedCharacterScale,
                $"{prefix}.perceivedCharacterScale",
                issues),
            EyeOffsetMode = ValidateEnum(
                source.EyeOffsetMode,
                defaults.EyeOffsetMode,
                $"{prefix}.eyeOffsetMode",
                issues),
            EyeOffsetMultiplier = ValidateRange(
                source.EyeOffsetMultiplier,
                0f,
                4f,
                defaults.EyeOffsetMultiplier,
                $"{prefix}.eyeOffsetMultiplier",
                issues),
            HeadTranslationMode = ValidateEnum(
                source.HeadTranslationMode,
                defaults.HeadTranslationMode,
                $"{prefix}.headTranslationMode",
                issues),
            HeadTranslationMultiplier = ValidateRange(
                source.HeadTranslationMultiplier,
                0f,
                4f,
                defaults.HeadTranslationMultiplier,
                $"{prefix}.headTranslationMultiplier",
                issues),
            LocomotionMode = ValidateEnum(
                source.LocomotionMode,
                defaults.LocomotionMode,
                $"{prefix}.locomotionMode",
                issues),
            LocomotionMultiplier = ValidateRange(
                source.LocomotionMultiplier,
                0f,
                4f,
                defaults.LocomotionMultiplier,
                $"{prefix}.locomotionMultiplier",
                issues)
        };
    }

    private static float ValidateRange(
        float value,
        float minimum,
        float maximum,
        float fallback,
        string name,
        List<string> issues)
    {
        if (float.IsFinite(value) && value >= minimum && value <= maximum)
        {
            return value;
        }

        issues.Add($"{name}:out-of-range");
        return fallback;
    }

    private static int ValidateRange(
        int value,
        int minimum,
        int maximum,
        int fallback,
        string name,
        List<string> issues)
    {
        if (value >= minimum && value <= maximum)
        {
            return value;
        }

        issues.Add($"{name}:out-of-range");
        return fallback;
    }

    private static T ValidateEnum<T>(T value, T fallback, string name, List<string> issues)
        where T : struct, Enum
    {
        if (Enum.IsDefined(typeof(T), value))
        {
            return value;
        }

        issues.Add($"{name}:unknown-value");
        return fallback;
    }

    private static int ValidateSnapAngle(
        int value,
        int fallback,
        List<string> issues)
    {
        if (value is 15 or 30 or 45 or 60)
        {
            return value;
        }

        issues.Add("tracking.viewSnapAngleDegrees:unsupported-value");
        return fallback;
    }

    private static string ValidateVisualEffectMode(
        string? value,
        string fallback,
        List<string> issues)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized == VrVisualEffectModes.AllOff)
        {
            return normalized;
        }

        issues.Add("render.visualEffectMode:unsupported-for-current-runtime");
        return fallback;
    }
}
