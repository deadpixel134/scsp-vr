namespace SongPrismVR.Core;

public sealed class OrientationSample
{
    public int Width { get; set; }

    public int Height { get; set; }

    public bool RenderTargetsValid { get; set; }

    public bool ExplicitChangeSignal { get; set; }

    // Unity Screen.orientation is diagnostic-only. The observed render dimensions
    // remain authoritative because Gakumas can keep returning a stale value.
    public int? ReportedScreenOrientation { get; set; }

    public string TargetSignature { get; set; } = string.Empty;

    public long NowMilliseconds { get; set; }
}

public sealed class OrientationDecision
{
    public OrientationDecision(
        OrientationTransitionState state,
        OrientationKind orientation,
        bool freezeFrame,
        bool blockPointerInput,
        bool requestRebind,
        bool timedOut)
    {
        State = state;
        Orientation = orientation;
        FreezeFrame = freezeFrame;
        BlockPointerInput = blockPointerInput;
        RequestRebind = requestRebind;
        TimedOut = timedOut;
    }

    public OrientationTransitionState State { get; }

    public OrientationKind Orientation { get; }

    public bool FreezeFrame { get; }

    public bool BlockPointerInput { get; }

    public bool RequestRebind { get; }

    public bool TimedOut { get; }
}

public sealed class OrientationStabilizer
{
    private readonly int _requiredStableFrames;
    private readonly long _timeoutMilliseconds;
    private OrientationKind _candidateOrientation;
    private string _candidateSignature = string.Empty;
    private string _stableSignature = string.Empty;
    private int _candidateFrames;
    private long _transitionStartedAt;

    public OrientationStabilizer(int requiredStableFrames = 5, long timeoutMilliseconds = 2000)
    {
        if (requiredStableFrames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredStableFrames));
        }

        if (timeoutMilliseconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        }

        _requiredStableFrames = requiredStableFrames;
        _timeoutMilliseconds = timeoutMilliseconds;
    }

    public OrientationTransitionState State { get; private set; } = OrientationTransitionState.SafePanel;

    public OrientationKind CurrentOrientation { get; private set; } = OrientationKind.Unknown;

    public OrientationDecision Observe(OrientationSample sample)
    {
        if (sample is null)
        {
            throw new ArgumentNullException(nameof(sample));
        }

        var observedOrientation = GetOrientation(sample.Width, sample.Height);

        if (CurrentOrientation == OrientationKind.Unknown &&
            sample.RenderTargetsValid &&
            observedOrientation != OrientationKind.Unknown)
        {
            CurrentOrientation = observedOrientation;
            _stableSignature = sample.TargetSignature;
            State = StableState(observedOrientation);
            return Decision(false, false, false);
        }

        if (State is OrientationTransitionState.StablePortrait or OrientationTransitionState.StableLandscape)
        {
            var inferredChange = observedOrientation != OrientationKind.Unknown &&
                                 observedOrientation != CurrentOrientation;
            var targetChanged = !string.Equals(
                sample.TargetSignature,
                _stableSignature,
                StringComparison.Ordinal);

            if (sample.ExplicitChangeSignal || inferredChange || targetChanged)
            {
                BeginWaiting(sample, observedOrientation);
                return Decision(true, true, false);
            }

            return Decision(false, false, false);
        }

        if (State == OrientationTransitionState.SafePanel)
        {
            if (sample.ExplicitChangeSignal)
            {
                BeginWaiting(sample, observedOrientation);
            }

            return Decision(true, State == OrientationTransitionState.WaitingForStableTargets, false);
        }

        if (State == OrientationTransitionState.Rebinding)
        {
            return Decision(true, true, false);
        }

        if (sample.NowMilliseconds - _transitionStartedAt >= _timeoutMilliseconds)
        {
            State = OrientationTransitionState.SafePanel;
            return Decision(true, false, false, timedOut: true);
        }

        if (!sample.RenderTargetsValid || observedOrientation == OrientationKind.Unknown)
        {
            _candidateFrames = 0;
            return Decision(true, true, false);
        }

        if (observedOrientation == _candidateOrientation &&
            string.Equals(sample.TargetSignature, _candidateSignature, StringComparison.Ordinal))
        {
            _candidateFrames++;
        }
        else
        {
            _candidateOrientation = observedOrientation;
            _candidateSignature = sample.TargetSignature;
            _candidateFrames = 1;
        }

        if (_candidateFrames >= _requiredStableFrames)
        {
            State = OrientationTransitionState.Rebinding;
            return Decision(true, true, true);
        }

        return Decision(true, true, false);
    }

    public OrientationDecision CompleteRebind(bool success)
    {
        if (State != OrientationTransitionState.Rebinding)
        {
            throw new InvalidOperationException("No orientation rebind is pending.");
        }

        if (!success)
        {
            State = OrientationTransitionState.SafePanel;
            return Decision(true, false, false);
        }

        CurrentOrientation = _candidateOrientation;
        _stableSignature = _candidateSignature;
        State = StableState(CurrentOrientation);
        return Decision(false, false, false);
    }

    public OrientationDecision BeginRecovery(OrientationSample sample)
    {
        if (sample is null)
        {
            throw new ArgumentNullException(nameof(sample));
        }
        BeginWaiting(sample, GetOrientation(sample.Width, sample.Height));
        return Decision(true, true, false);
    }

    private void BeginWaiting(OrientationSample sample, OrientationKind orientation)
    {
        State = OrientationTransitionState.WaitingForStableTargets;
        _transitionStartedAt = sample.NowMilliseconds;
        _candidateOrientation = orientation;
        _candidateSignature = sample.TargetSignature;
        _candidateFrames = sample.RenderTargetsValid && orientation != OrientationKind.Unknown ? 1 : 0;
    }

    private OrientationDecision Decision(
        bool freezeFrame,
        bool blockPointerInput,
        bool requestRebind,
        bool timedOut = false)
    {
        var reportedOrientation = State == OrientationTransitionState.Rebinding
            ? _candidateOrientation
            : CurrentOrientation;

        return new OrientationDecision(
            State,
            reportedOrientation,
            freezeFrame,
            blockPointerInput,
            requestRebind,
            timedOut);
    }

    private static OrientationKind GetOrientation(int width, int height)
    {
        if (width <= 0 || height <= 0 || width == height)
        {
            return OrientationKind.Unknown;
        }

        return width > height ? OrientationKind.Landscape : OrientationKind.Portrait;
    }

    private static OrientationTransitionState StableState(OrientationKind orientation)
    {
        return orientation == OrientationKind.Landscape
            ? OrientationTransitionState.StableLandscape
            : OrientationTransitionState.StablePortrait;
    }
}
