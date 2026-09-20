using SongPrismVR.Core;

namespace Doorstop;

internal readonly record struct OpenXrLocomotionStateSnapshot(
    float AxisX,
    float AxisY,
    float ViewTurnAxisX,
    float ViewTurnAxisY,
    float WorldDragDeltaX,
    float WorldDragDeltaY,
    float WorldDragDeltaZ);

internal static class OpenXrLocomotionStateRegistry
{
    private static readonly object Sync = new();
    private static float _axisX;
    private static float _axisY;
    private static float _viewTurnAxisX;
    private static float _viewTurnAxisY;
    private static float _worldDragDeltaX;
    private static float _worldDragDeltaY;
    private static float _worldDragDeltaZ;
    private static long _updatedMilliseconds;

    public static void Update(
        bool locomotionActive,
        float axisX,
        float axisY,
        bool viewTurnActive,
        float viewTurnAxisX,
        float viewTurnAxisY)
    {
        lock (Sync)
        {
            _axisX = locomotionActive && float.IsFinite(axisX) ? axisX : 0f;
            _axisY = locomotionActive && float.IsFinite(axisY) ? axisY : 0f;
            _viewTurnAxisX = viewTurnActive && float.IsFinite(viewTurnAxisX)
                ? viewTurnAxisX
                : 0f;
            _viewTurnAxisY = viewTurnActive && float.IsFinite(viewTurnAxisY)
                ? viewTurnAxisY
                : 0f;
            _updatedMilliseconds = Environment.TickCount64;
        }
    }

    public static void AddWorldDragDelta(TrackingVector3 delta)
    {
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y) ||
            !float.IsFinite(delta.Z))
        {
            return;
        }

        lock (Sync)
        {
            _worldDragDeltaX += delta.X;
            _worldDragDeltaY += delta.Y;
            _worldDragDeltaZ += delta.Z;
            _updatedMilliseconds = Environment.TickCount64;
        }
    }

    public static OpenXrLocomotionStateSnapshot? Snapshot(
        int maximumAgeMilliseconds)
    {
        lock (Sync)
        {
            if (_updatedMilliseconds == 0 ||
                Environment.TickCount64 - _updatedMilliseconds > maximumAgeMilliseconds)
            {
                _worldDragDeltaX = 0f;
                _worldDragDeltaY = 0f;
                _worldDragDeltaZ = 0f;
                return null;
            }

            OpenXrLocomotionStateSnapshot snapshot = new(
                _axisX,
                _axisY,
                _viewTurnAxisX,
                _viewTurnAxisY,
                _worldDragDeltaX,
                _worldDragDeltaY,
                _worldDragDeltaZ);
            _worldDragDeltaX = 0f;
            _worldDragDeltaY = 0f;
            _worldDragDeltaZ = 0f;
            return snapshot;
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            _axisX = 0f;
            _axisY = 0f;
            _viewTurnAxisX = 0f;
            _viewTurnAxisY = 0f;
            _worldDragDeltaX = 0f;
            _worldDragDeltaY = 0f;
            _worldDragDeltaZ = 0f;
            _updatedMilliseconds = 0;
        }
    }
}
