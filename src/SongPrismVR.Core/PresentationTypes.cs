namespace SongPrismVR.Core;

public enum PresentationContext
{
    Transition,
    Menu2D,
    Video2D,
    CommuCandidate,
    LiveCandidate,
    Unknown
}
public enum RecommendedPresentationMode
{
    FrozenPanel,
    SafePanel,
    Immersive
}

public enum OrientationKind
{
    Unknown,
    Portrait,
    Landscape
}

public enum OrientationTransitionState
{
    StablePortrait,
    StableLandscape,
    WaitingForStableTargets,
    Rebinding,
    SafePanel
}
