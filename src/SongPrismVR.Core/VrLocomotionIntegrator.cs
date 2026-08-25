namespace SongPrismVR.Core;

public sealed class VrLocomotionIntegrator
{
    public TrackingVector3 Offset { get; private set; }

    public bool Update(
        float axisX,
        float axisY,
        TrackingQuaternion headRotation,
        float deltaSeconds,
        float speedMetersPerSecond,
        float deadzone = 0.20f)
    {
        if (!IsFinite(axisX) || !IsFinite(axisY) ||
            !IsFinite(deltaSeconds) || deltaSeconds < 0f ||
            !IsFinite(speedMetersPerSecond) || speedMetersPerSecond < 0f ||
            !IsFinite(deadzone) || deadzone < 0f || deadzone >= 1f ||
            !TryNormalize(headRotation, out TrackingQuaternion rotation))
        {
            return false;
        }

        float magnitude = MathF.Sqrt((axisX * axisX) + (axisY * axisY));
        if (magnitude <= deadzone || deltaSeconds == 0f || speedMetersPerSecond == 0f)
        {
            return true;
        }

        float clampedMagnitude = MathF.Min(magnitude, 1f);
        float scaledMagnitude = (clampedMagnitude - deadzone) / (1f - deadzone);
        float normalizedX = axisX / magnitude;
        float normalizedY = axisY / magnitude;

        TrackingVector3 forward = Rotate(
            rotation,
            new TrackingVector3(0f, 0f, 1f));
        TrackingVector3 right = Rotate(
            rotation,
            new TrackingVector3(1f, 0f, 0f));
        float distance = scaledMagnitude * speedMetersPerSecond *
            MathF.Min(deltaSeconds, 0.10f);
        Offset = Add(
            Offset,
            Scale(
                Add(Scale(right, normalizedX), Scale(forward, normalizedY)),
                distance));
        return true;
    }

    public void Reset() => Offset = default;

    private static TrackingVector3 Rotate(
        TrackingQuaternion rotation,
        TrackingVector3 value)
    {
        TrackingVector3 q = new(rotation.X, rotation.Y, rotation.Z);
        TrackingVector3 t = Scale(Cross(q, value), 2f);
        return Add(value, Add(Scale(t, rotation.W), Cross(q, t)));
    }

    private static bool TryNormalize(
        TrackingQuaternion value,
        out TrackingQuaternion normalized)
    {
        normalized = default;
        if (!IsFinite(value.X) || !IsFinite(value.Y) ||
            !IsFinite(value.Z) || !IsFinite(value.W))
        {
            return false;
        }

        float magnitudeSquared =
            (value.X * value.X) + (value.Y * value.Y) +
            (value.Z * value.Z) + (value.W * value.W);
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

    private static TrackingVector3 Add(TrackingVector3 left, TrackingVector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static TrackingVector3 Scale(TrackingVector3 value, float scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    private static TrackingVector3 Cross(TrackingVector3 left, TrackingVector3 right) =>
        new(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}
