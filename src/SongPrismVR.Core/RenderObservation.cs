namespace SongPrismVR.Core;

public sealed class RenderObservation
{
    public bool IsOrientationChanging { get; set; }

    public bool IsSceneChanging { get; set; }

    public bool AreRenderTargetsStable { get; set; }

    public int StableFrameCount { get; set; }

    public bool HasFullScreenVideo { get; set; }

    public bool HasActiveWebView { get; set; }

    public bool HasLiveCameraMarker { get; set; }

    public bool HasUguissMarker { get; set; }

    public bool HasCinemachineOutput { get; set; }

    public bool HasValidWorldCamera { get; set; }

    public bool IsUrpCameraStackValid { get; set; }

    public bool UiCanvasDominates { get; set; }

    public bool IsProfileApproved { get; set; }

    public int WorldCameraCount { get; set; }
}

public sealed class ClassificationResult
{
    public ClassificationResult(
        PresentationContext context,
        RecommendedPresentationMode mode,
        string reason)
    {
        Context = context;
        Mode = mode;
        Reason = reason;
    }

    public PresentationContext Context { get; }

    public RecommendedPresentationMode Mode { get; }

    public string Reason { get; }
}
