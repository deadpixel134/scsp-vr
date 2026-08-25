namespace SongPrismVR.Core;

public sealed class SceneClassifier
{
    private readonly int _requiredStableFrames;

    public SceneClassifier(int requiredStableFrames = 5)
    {
        if (requiredStableFrames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredStableFrames));
        }

        _requiredStableFrames = requiredStableFrames;
    }

    public ClassificationResult Classify(RenderObservation observation)
    {
        if (observation is null)
        {
            throw new ArgumentNullException(nameof(observation));
        }

        if (observation.IsOrientationChanging ||
            observation.IsSceneChanging ||
            !observation.AreRenderTargetsStable ||
            observation.StableFrameCount < _requiredStableFrames)
        {
            return new ClassificationResult(
                PresentationContext.Transition,
                RecommendedPresentationMode.FrozenPanel,
                "Scene, orientation, or render targets are not stable.");
        }

        if (observation.HasFullScreenVideo || observation.HasActiveWebView)
        {
            return new ClassificationResult(
                PresentationContext.Video2D,
                RecommendedPresentationMode.SafePanel,
                "VideoPlayer or WebView output takes priority over stereo rendering.");
        }

        if (observation.HasLiveCameraMarker &&
            observation.HasValidWorldCamera &&
            observation.IsUrpCameraStackValid)
        {
            return Candidate(
                PresentationContext.LiveCandidate,
                observation.IsProfileApproved,
                "Live camera markers and a valid world camera stack were detected.");
        }

        if (observation.HasUguissMarker &&
            observation.HasCinemachineOutput &&
            observation.HasValidWorldCamera)
        {
            return Candidate(
                PresentationContext.CommuCandidate,
                observation.IsProfileApproved,
                "Uguiss and Cinemachine world-camera output were detected.");
        }

        if (observation.UiCanvasDominates ||
            !observation.HasValidWorldCamera ||
            observation.WorldCameraCount == 0)
        {
            return new ClassificationResult(
                PresentationContext.Menu2D,
                RecommendedPresentationMode.SafePanel,
                "The frame is UI-dominant or has no valid world camera.");
        }

        return new ClassificationResult(
            PresentationContext.Unknown,
            RecommendedPresentationMode.SafePanel,
            "The render structure does not match an approved presentation profile.");
    }

    private static ClassificationResult Candidate(
        PresentationContext context,
        bool profileApproved,
        string reason)
    {
        return new ClassificationResult(
            context,
            profileApproved
                ? RecommendedPresentationMode.Immersive
                : RecommendedPresentationMode.SafePanel,
            profileApproved
                ? reason + " The matching profile is approved."
                : reason + " The matching profile is not approved, so panel fallback is required.");
    }
}
