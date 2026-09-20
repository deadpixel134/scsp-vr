namespace SongPrismVR.Core;

public enum VrWorldDragActivation
{
    LeftGrip,
    LeftTrigger,
    RightGrip,
    RightTrigger
}

public readonly struct VrWorldDragFrame
{
    public VrWorldDragFrame(
        float leftGrip,
        float leftTrigger,
        float rightGrip,
        float rightTrigger,
        bool leftGripConsumed,
        bool leftTriggerConsumed,
        bool rightGripConsumed,
        bool rightTriggerConsumed,
        bool leftPoseTracked,
        TrackingVector3 leftPosition,
        bool rightPoseTracked,
        TrackingVector3 rightPosition) =>
        (LeftGrip, LeftTrigger, RightGrip, RightTrigger,
            LeftGripConsumed, LeftTriggerConsumed,
            RightGripConsumed, RightTriggerConsumed,
            LeftPoseTracked, LeftPosition, RightPoseTracked, RightPosition) =
        (leftGrip, leftTrigger, rightGrip, rightTrigger,
            leftGripConsumed, leftTriggerConsumed,
            rightGripConsumed, rightTriggerConsumed,
            leftPoseTracked, leftPosition, rightPoseTracked, rightPosition);

    public float LeftGrip { get; }
    public float LeftTrigger { get; }
    public float RightGrip { get; }
    public float RightTrigger { get; }
    public bool LeftGripConsumed { get; }
    public bool LeftTriggerConsumed { get; }
    public bool RightGripConsumed { get; }
    public bool RightTriggerConsumed { get; }
    public bool LeftPoseTracked { get; }
    public TrackingVector3 LeftPosition { get; }
    public bool RightPoseTracked { get; }
    public TrackingVector3 RightPosition { get; }
}

public sealed class VrWorldDragState
{
    private const float PressThreshold = 0.72f;
    private const float ReleaseThreshold = 0.25f;

    private VrWorldDragActivation? _owner;
    private VrHand _trackingHand;
    private TrackingVector3 _previousPosition;
    private readonly bool[] _suppressedUntilRelease = new bool[4];
    private readonly float[] _previousValues = new float[4];

    public bool Active => _owner.HasValue;

    public VrWorldDragActivation? Owner => _owner;

    public VrHand TrackingHand => _trackingHand;

    public bool Update(
        VrWorldDragSettings settings,
        VrWorldDragFrame frame,
        out TrackingVector3 delta)
    {
        delta = default;
        Span<float> values = stackalloc float[4]
        {
            FiniteOrZero(frame.LeftGrip),
            FiniteOrZero(frame.LeftTrigger),
            FiniteOrZero(frame.RightGrip),
            FiniteOrZero(frame.RightTrigger)
        };
        Span<bool> consumed = stackalloc bool[4]
        {
            frame.LeftGripConsumed,
            frame.LeftTriggerConsumed,
            frame.RightGripConsumed,
            frame.RightTriggerConsumed
        };

        for (int index = 0; index < values.Length; index++)
        {
            if (consumed[index] && values[index] > ReleaseThreshold)
            {
                _suppressedUntilRelease[index] = true;
            }
            if (values[index] <= ReleaseThreshold)
            {
                _suppressedUntilRelease[index] = false;
            }
        }

        if (!settings.Enabled)
        {
            ResetOwner();
            StorePrevious(values);
            return false;
        }

        if (_owner is VrWorldDragActivation owner)
        {
            int ownerIndex = (int)owner;
            if (consumed[ownerIndex] ||
                values[ownerIndex] <= ReleaseThreshold ||
                !TryGetTrackedPosition(frame, _trackingHand, out TrackingVector3 position))
            {
                if (values[ownerIndex] > ReleaseThreshold)
                {
                    _suppressedUntilRelease[ownerIndex] = true;
                }
                ResetOwner();
                StorePrevious(values);
                return false;
            }

            delta = Subtract(position, _previousPosition);
            _previousPosition = position;
            StorePrevious(values);
            return IsFinite(delta);
        }

        for (int index = 0; index < values.Length; index++)
        {
            VrWorldDragActivation activation = (VrWorldDragActivation)index;
            if (!Enabled(settings, activation) || consumed[index] ||
                _suppressedUntilRelease[index] ||
                values[index] < PressThreshold ||
                _previousValues[index] >= PressThreshold)
            {
                continue;
            }

            VrHand activationHand = HandFor(activation);
            VrHand trackingHand = SelectTrackingHand(settings, activationHand);
            if (!TryGetTrackedPosition(frame, trackingHand, out TrackingVector3 position))
            {
                _suppressedUntilRelease[index] = true;
                continue;
            }

            _owner = activation;
            _trackingHand = trackingHand;
            _previousPosition = position;
            break;
        }

        StorePrevious(values);
        return _owner.HasValue;
    }

    public void Reset()
    {
        ResetOwner();
        Array.Clear(_suppressedUntilRelease, 0, _suppressedUntilRelease.Length);
        Array.Clear(_previousValues, 0, _previousValues.Length);
    }

    private static VrHand SelectTrackingHand(
        VrWorldDragSettings settings,
        VrHand activationHand)
    {
        if (activationHand == VrHand.Left && settings.TrackLeftHand)
        {
            return VrHand.Left;
        }
        if (activationHand == VrHand.Right && settings.TrackRightHand)
        {
            return VrHand.Right;
        }
        return settings.TrackLeftHand ? VrHand.Left : VrHand.Right;
    }

    private static bool TryGetTrackedPosition(
        VrWorldDragFrame frame,
        VrHand hand,
        out TrackingVector3 position)
    {
        bool tracked = hand == VrHand.Left
            ? frame.LeftPoseTracked
            : frame.RightPoseTracked;
        position = hand == VrHand.Left
            ? frame.LeftPosition
            : frame.RightPosition;
        return tracked && IsFinite(position);
    }

    private static bool Enabled(
        VrWorldDragSettings settings,
        VrWorldDragActivation activation) => activation switch
        {
            VrWorldDragActivation.LeftGrip => settings.LeftGripActivation,
            VrWorldDragActivation.LeftTrigger => settings.LeftTriggerActivation,
            VrWorldDragActivation.RightGrip => settings.RightGripActivation,
            VrWorldDragActivation.RightTrigger => settings.RightTriggerActivation,
            _ => false
        };

    private static VrHand HandFor(VrWorldDragActivation activation) =>
        activation is VrWorldDragActivation.LeftGrip or VrWorldDragActivation.LeftTrigger
            ? VrHand.Left
            : VrHand.Right;

    private void ResetOwner()
    {
        _owner = null;
        _previousPosition = default;
    }

    private void StorePrevious(ReadOnlySpan<float> values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            _previousValues[index] = values[index];
        }
    }

    private static TrackingVector3 Subtract(
        TrackingVector3 left,
        TrackingVector3 right) => new(
            left.X - right.X,
            left.Y - right.Y,
            left.Z - right.Z);

    private static bool IsFinite(TrackingVector3 value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    private static float FiniteOrZero(float value) => IsFinite(value) ? value : 0f;

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}
