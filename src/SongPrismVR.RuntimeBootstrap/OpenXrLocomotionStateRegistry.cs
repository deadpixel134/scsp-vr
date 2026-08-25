namespace Doorstop;

internal readonly record struct OpenXrLocomotionStateSnapshot(
    float AxisX,
    float AxisY,
    float ViewTurnAxisX,
    float ViewTurnAxisY);

internal static class OpenXrLocomotionStateRegistry
{
    private static readonly object Sync = new();
    private static float _axisX;
    private static float _axisY;
    private static float _viewTurnAxisX;
    private static float _viewTurnAxisY;
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

    public static OpenXrLocomotionStateSnapshot? Snapshot(
        int maximumAgeMilliseconds)
    {
        lock (Sync)
        {
            if (_updatedMilliseconds == 0 ||
                Environment.TickCount64 - _updatedMilliseconds > maximumAgeMilliseconds)
            {
                return null;
            }

            return new OpenXrLocomotionStateSnapshot(
                _axisX,
                _axisY,
                _viewTurnAxisX,
                _viewTurnAxisY);
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
            _updatedMilliseconds = 0;
        }
    }
}
