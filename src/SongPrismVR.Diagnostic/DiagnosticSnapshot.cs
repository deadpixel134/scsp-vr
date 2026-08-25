using System.Text.Json;

namespace SongPrismVR.Diagnostic;

internal sealed class DiagnosticSnapshot
{
    public DateTimeOffset TimestampUtc { get; set; }

    public string Scene { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public string Orientation { get; set; } = string.Empty;

    public string ScreenOrientation { get; set; } = string.Empty;

    public int CameraCount { get; set; }

    public List<string> ActiveCameraNames { get; set; } = new();

    public int WorldCameraCount { get; set; }

    public int ActiveCanvasCount { get; set; }

    public int OverlayCanvasCount { get; set; }

    public int PlayingVideoCount { get; set; }

    public List<string> MarkerTypeNames { get; set; } = new();

    public int StableFrameCount { get; set; }

    public string Context { get; set; } = string.Empty;

    public string RecommendedMode { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string EventSignature() => JsonSerializer.Serialize(new
    {
        Scene,
        Width,
        Height,
        Orientation,
        ActiveCameraNames,
        WorldCameraCount,
        ActiveCanvasCount,
        OverlayCanvasCount,
        PlayingVideoCount,
        MarkerTypeNames,
        Context,
        RecommendedMode
    });
}
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Instance = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
