namespace SongPrismVR.Core;

public readonly struct TrackingVector3
{
    public TrackingVector3(float x, float y, float z) => (X, Y, Z) = (x, y, z);

    public float X { get; }
    public float Y { get; }
    public float Z { get; }
}

public readonly struct TrackingQuaternion
{
    public TrackingQuaternion(float x, float y, float z, float w) =>
        (X, Y, Z, W) = (x, y, z, w);

    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float W { get; }
}

public readonly struct TrackingEyePose
{
    public TrackingEyePose(TrackingVector3 position, TrackingQuaternion orientation) =>
        (Position, Orientation) = (position, orientation);

    public TrackingVector3 Position { get; }
    public TrackingQuaternion Orientation { get; }
}

public readonly struct TrackingStereoPose
{
    public TrackingStereoPose(TrackingEyePose left, TrackingEyePose right) =>
        (Left, Right) = (left, right);

    public TrackingEyePose Left { get; }
    public TrackingEyePose Right { get; }
}

public readonly struct UnityEyePose
{
    public UnityEyePose(
        TrackingVector3 localPosition,
        TrackingQuaternion localRotation,
        float yawDeltaRadians,
        float pitchDeltaRadians,
        float rollDeltaRadians)
    {
        LocalPosition = localPosition;
        LocalRotation = localRotation;
        YawDeltaRadians = yawDeltaRadians;
        PitchDeltaRadians = pitchDeltaRadians;
        RollDeltaRadians = rollDeltaRadians;
    }

    public TrackingVector3 LocalPosition { get; }
    public TrackingQuaternion LocalRotation { get; }

    public float YawDeltaRadians { get; }

    public float PitchDeltaRadians { get; }

    public float RollDeltaRadians { get; }
}

public readonly struct UnityStereoPose
{
    public UnityStereoPose(UnityEyePose left, UnityEyePose right) =>
        (Left, Right) = (left, right);

    public UnityEyePose Left { get; }
    public UnityEyePose Right { get; }
}

public sealed class SixDofPoseMapper
{
    private TrackingVector3 _originPosition;
    private TrackingQuaternion _originOrientation;
    private float _originUnityYaw;
    private float _originUnityPitch;
    private float _originUnityRoll;

    public bool HasOrigin { get; private set; }

    public bool TryMap(
        TrackingStereoPose tracked,
        float headTranslationScale,
        float eyeOffsetScale,
        out UnityStereoPose mapped,
        bool preserveTrackingSpaceOrientation = false)
    {
        mapped = default;
        if (!IsFinite(headTranslationScale) || headTranslationScale < 0f ||
            !IsFinite(eyeOffsetScale) || eyeOffsetScale < 0f ||
            !TryNormalize(tracked.Left.Orientation, out TrackingQuaternion leftOrientation) ||
            !TryNormalize(tracked.Right.Orientation, out TrackingQuaternion rightOrientation) ||
            !IsFinite(tracked.Left.Position) || !IsFinite(tracked.Right.Position))
        {
            return false;
        }

        TrackingVector3 center = Scale(
            Add(tracked.Left.Position, tracked.Right.Position),
            0.5f);
        TrackingQuaternion centerOrientation = AverageOrientation(
            leftOrientation,
            rightOrientation);
        if (!HasOrigin)
        {
            _originPosition = center;
            _originOrientation = centerOrientation;
            ToYawPitchRoll(
                ToUnityRotation(centerOrientation),
                out _originUnityYaw,
                out _originUnityPitch,
                out _originUnityRoll);
            HasOrigin = true;
        }

        TrackingQuaternion inverseOrigin = Conjugate(_originOrientation);
        TrackingVector3 headTranslation = Subtract(center, _originPosition);
        TrackingVector3 leftEyeOffset = Subtract(tracked.Left.Position, center);
        TrackingVector3 rightEyeOffset = Subtract(tracked.Right.Position, center);
        if (!preserveTrackingSpaceOrientation)
        {
            headTranslation = Rotate(inverseOrigin, headTranslation);
            leftEyeOffset = Rotate(inverseOrigin, leftEyeOffset);
            rightEyeOffset = Rotate(inverseOrigin, rightEyeOffset);
        }

        TrackingVector3 leftLocal = Add(
            Scale(headTranslation, headTranslationScale),
            Scale(leftEyeOffset, eyeOffsetScale));
        TrackingVector3 rightLocal = Add(
            Scale(headTranslation, headTranslationScale),
            Scale(rightEyeOffset, eyeOffsetScale));
        TrackingQuaternion leftMappedOrientation = preserveTrackingSpaceOrientation
            ? leftOrientation
            : Normalize(Multiply(inverseOrigin, leftOrientation));
        TrackingQuaternion rightMappedOrientation = preserveTrackingSpaceOrientation
            ? rightOrientation
            : Normalize(Multiply(inverseOrigin, rightOrientation));
        ToYawPitchRoll(
            ToUnityRotation(leftOrientation),
            out float leftYaw,
            out float leftPitch,
            out float leftRoll);
        ToYawPitchRoll(
            ToUnityRotation(rightOrientation),
            out float rightYaw,
            out float rightPitch,
            out float rightRoll);

        mapped = new UnityStereoPose(
            new UnityEyePose(
                ToUnityPosition(leftLocal),
                ToUnityRotation(leftMappedOrientation),
                WrapAngle(leftYaw - (preserveTrackingSpaceOrientation ? 0f : _originUnityYaw)),
                leftPitch - (preserveTrackingSpaceOrientation ? 0f : _originUnityPitch),
                WrapAngle(leftRoll - (preserveTrackingSpaceOrientation ? 0f : _originUnityRoll))),
            new UnityEyePose(
                ToUnityPosition(rightLocal),
                ToUnityRotation(rightMappedOrientation),
                WrapAngle(rightYaw - (preserveTrackingSpaceOrientation ? 0f : _originUnityYaw)),
                rightPitch - (preserveTrackingSpaceOrientation ? 0f : _originUnityPitch),
                WrapAngle(rightRoll - (preserveTrackingSpaceOrientation ? 0f : _originUnityRoll))));
        return true;
    }

    public void Reset()
    {
        _originPosition = default;
        _originOrientation = default;
        _originUnityYaw = 0f;
        _originUnityPitch = 0f;
        _originUnityRoll = 0f;
        HasOrigin = false;
    }

    private static TrackingQuaternion AverageOrientation(
        TrackingQuaternion left,
        TrackingQuaternion right)
    {
        if (Dot(left, right) < 0f)
        {
            right = new TrackingQuaternion(-right.X, -right.Y, -right.Z, -right.W);
        }

        return Normalize(new TrackingQuaternion(
            left.X + right.X,
            left.Y + right.Y,
            left.Z + right.Z,
            left.W + right.W));
    }

    private static TrackingVector3 ToUnityPosition(TrackingVector3 value) =>
        new(value.X, value.Y, -value.Z);

    private static TrackingQuaternion ToUnityRotation(TrackingQuaternion value) =>
        new(-value.X, -value.Y, value.Z, value.W);

    private static void ToYawPitchRoll(
        TrackingQuaternion rotation,
        out float yaw,
        out float pitch,
        out float roll)
    {
        TrackingVector3 forward = Rotate(
            rotation,
            new TrackingVector3(0f, 0f, 1f));
        TrackingVector3 right = Rotate(
            rotation,
            new TrackingVector3(1f, 0f, 0f));
        TrackingVector3 up = Rotate(
            rotation,
            new TrackingVector3(0f, 1f, 0f));
        float horizontalLength = MathF.Sqrt(
            (forward.X * forward.X) + (forward.Z * forward.Z));
        yaw = horizontalLength > 0.0001f
            ? MathF.Atan2(forward.X, forward.Z)
            : MathF.Atan2(-right.Z, right.X);
        pitch = MathF.Atan2(-forward.Y, horizontalLength);
        roll = MathF.Atan2(right.Y, up.Y);
    }

    private static float WrapAngle(float value)
    {
        while (value > MathF.PI)
        {
            value -= MathF.PI * 2f;
        }
        while (value < -MathF.PI)
        {
            value += MathF.PI * 2f;
        }
        return value;
    }

    private static TrackingVector3 Rotate(
        TrackingQuaternion rotation,
        TrackingVector3 value)
    {
        TrackingVector3 q = new(rotation.X, rotation.Y, rotation.Z);
        TrackingVector3 t = Scale(Cross(q, value), 2f);
        return Add(value, Add(Scale(t, rotation.W), Cross(q, t)));
    }

    private static TrackingQuaternion Multiply(
        TrackingQuaternion left,
        TrackingQuaternion right) => new(
            (left.W * right.X) + (left.X * right.W) +
                (left.Y * right.Z) - (left.Z * right.Y),
            (left.W * right.Y) - (left.X * right.Z) +
                (left.Y * right.W) + (left.Z * right.X),
            (left.W * right.Z) + (left.X * right.Y) -
                (left.Y * right.X) + (left.Z * right.W),
            (left.W * right.W) - (left.X * right.X) -
                (left.Y * right.Y) - (left.Z * right.Z));

    private static TrackingQuaternion Conjugate(TrackingQuaternion value) =>
        new(-value.X, -value.Y, -value.Z, value.W);

    private static bool TryNormalize(
        TrackingQuaternion value,
        out TrackingQuaternion normalized)
    {
        normalized = default;
        if (!IsFinite(value))
        {
            return false;
        }

        float magnitudeSquared = Dot(value, value);
        if (magnitudeSquared < 0.000001f)
        {
            return false;
        }

        float inverseMagnitude = 1f / MathF.Sqrt(magnitudeSquared);
        normalized = new TrackingQuaternion(
            value.X * inverseMagnitude,
            value.Y * inverseMagnitude,
            value.Z * inverseMagnitude,
            value.W * inverseMagnitude);
        return true;
    }

    private static TrackingQuaternion Normalize(TrackingQuaternion value)
    {
        _ = TryNormalize(value, out TrackingQuaternion normalized);
        return normalized;
    }

    private static float Dot(TrackingQuaternion left, TrackingQuaternion right) =>
        (left.X * right.X) + (left.Y * right.Y) +
        (left.Z * right.Z) + (left.W * right.W);

    private static TrackingVector3 Add(TrackingVector3 left, TrackingVector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static TrackingVector3 Subtract(TrackingVector3 left, TrackingVector3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static TrackingVector3 Scale(TrackingVector3 value, float scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    private static TrackingVector3 Cross(TrackingVector3 left, TrackingVector3 right) =>
        new(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));

    private static bool IsFinite(TrackingVector3 value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    private static bool IsFinite(TrackingQuaternion value) =>
        IsFinite(value.X) && IsFinite(value.Y) &&
        IsFinite(value.Z) && IsFinite(value.W);

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}
