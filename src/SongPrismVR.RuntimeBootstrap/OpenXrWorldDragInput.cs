using SongPrismVR.Core;

namespace Doorstop;

internal sealed class OpenXrWorldDragInput
{
    private const int MaximumFrameGapMilliseconds = 250;
    private readonly VrWorldDragSettings _settings;
    private readonly VrPanelSettings _panelSettings;
    private readonly VrInputSettings _inputSettings;
    private readonly VrWorldDragState _state = new();
    private long _lastUpdateMilliseconds;

    public OpenXrWorldDragInput(VrSettings settings)
    {
        _settings = settings.Tracking.WorldDrag;
        _panelSettings = settings.Panel;
        _inputSettings = settings.Input;
    }

    public void Update(
        OpenXrControllerFrame frame,
        bool pointerHitPresentedPanel,
        bool immersiveWorldAvailable)
    {
        if (!immersiveWorldAvailable)
        {
            Reset();
            return;
        }

        long now = Environment.TickCount64;
        if (_lastUpdateMilliseconds != 0 &&
            now - _lastUpdateMilliseconds > MaximumFrameGapMilliseconds)
        {
            _state.Reset();
        }
        _lastUpdateMilliseconds = now;

        bool panelConsumesLeftGrip =
            _panelSettings.ToggleBinding == PanelToggleBinding.Grip &&
            _panelSettings.PanelHand == VrHand.Left;
        bool panelConsumesRightGrip =
            _panelSettings.ToggleBinding == PanelToggleBinding.Grip &&
            _panelSettings.PanelHand == VrHand.Right;
        bool pointerConsumesTrigger = pointerHitPresentedPanel &&
            _inputSettings.TriggerClickEnabled;
        bool pointerConsumesLeftTrigger = pointerConsumesTrigger &&
            _panelSettings.PointerHand == VrHand.Left;
        bool pointerConsumesRightTrigger = pointerConsumesTrigger &&
            _panelSettings.PointerHand == VrHand.Right;

        bool wasActive = _state.Active;
        _ = _state.Update(
            _settings,
            new VrWorldDragFrame(
                frame.LeftGripValue,
                frame.LeftTriggerValue,
                frame.RightGripValue,
                frame.RightTriggerValue,
                panelConsumesLeftGrip,
                pointerConsumesLeftTrigger,
                panelConsumesRightGrip,
                pointerConsumesRightTrigger,
                frame.LeftGripPoseTracked,
                Position(frame.LeftGripPose),
                frame.RightGripPoseTracked,
                Position(frame.RightGripPose)),
            out TrackingVector3 delta);
        if (delta.X != 0f || delta.Y != 0f || delta.Z != 0f)
        {
            OpenXrLocomotionStateRegistry.AddWorldDragDelta(delta);
        }

        if (wasActive != _state.Active)
        {
            RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = _state.Active
                    ? "controller-world-drag-started"
                    : "controller-world-drag-stopped",
                BootstrapVersion = RuntimeProbe.BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                Reason = _state.Active
                    ? $"activation={_state.Owner};trackingHand={_state.TrackingHand};scale=one-to-one"
                    : "The activation was released, consumed, stale, or lost tracking."
            });
        }
    }

    public void Reset()
    {
        _state.Reset();
        _lastUpdateMilliseconds = 0;
    }

    private static TrackingVector3 Position(OpenXrControllerPose pose) =>
        new(pose.PositionX, pose.PositionY, pose.PositionZ);
}
