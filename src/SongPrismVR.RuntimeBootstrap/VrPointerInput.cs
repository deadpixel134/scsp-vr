using System.Diagnostics;
using System.Runtime.InteropServices;
using SongPrismVR.Core;

namespace Doorstop;

internal readonly record struct VrPointerVisual(bool Visible, float U, float V);

internal sealed class VrPointerInput : IDisposable
{
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseWheel = 0x0800;
    private const byte VirtualKeyEscape = 0x1B;
    private const uint KeyUp = 0x0002;
    private const float TriggerDragDistanceSquared = 0.035f * 0.035f;
    private static readonly long TriggerDragDelayTicks = Stopwatch.Frequency * 120 / 1000;
    private static readonly long ScrollRepeatTicks = Stopwatch.Frequency * 80 / 1000;

    private readonly AnalogPressLatch _triggerLatch = new(0.15f, 0.72f, 0.10f, 0.25f);
    private readonly VrInputSettings _settings;
    private PrimarySource _primarySource;
    private bool _lastPrimaryPressed;
    private bool _lastSecondaryPressed;
    private bool _triggerCoordinateLatched;
    private bool _triggerDragging;
    private bool _suppressTriggerUntilReleased;
    private float _latchedU;
    private float _latchedV;
    private float _lastU;
    private float _lastV;
    private long _triggerPressedTimestamp;
    private long _nextScrollTimestamp;
    private DateTimeOffset _nextBlockedLogUtc = DateTimeOffset.MinValue;
    private bool _readyLogged;
    private IntPtr _gameWindow;

    public VrPointerInput(VrInputSettings settings)
    {
        _settings = settings;
    }

    public VrPointerVisual Update(
        bool pointerAvailable,
        float u,
        float v,
        OpenXrControllerFrame controllerFrame)
    {
        bool primaryRising = controllerFrame.PointerPrimaryPressed && !_lastPrimaryPressed;
        bool primaryFalling = !controllerFrame.PointerPrimaryPressed && _lastPrimaryPressed;
        bool secondaryRising = controllerFrame.PointerBackPressed && !_lastSecondaryPressed;
        _lastPrimaryPressed = controllerFrame.PointerPrimaryPressed;
        _lastSecondaryPressed = controllerFrame.PointerBackPressed;
        float triggerValue = _settings.TriggerClickEnabled
            ? controllerFrame.PointerTriggerValue
            : 0f;

        if (!pointerAvailable || !TryResolveForegroundClientPoint(u, v, out Point screenPoint))
        {
            ReleasePrimaryIfNeeded();
            _ = _triggerLatch.Cancel();
            _suppressTriggerUntilReleased |= triggerValue > 0.10f;
            _triggerCoordinateLatched = false;
            _triggerDragging = false;
            return default;
        }

        _lastU = u;
        _lastV = v;
        bool holdTriggerCoordinate = _triggerCoordinateLatched &&
            (_triggerLatch.IsArmed || _primarySource == PrimarySource.Trigger) &&
            !_triggerDragging;
        if (holdTriggerCoordinate &&
            TryResolveForegroundClientPoint(_latchedU, _latchedV, out Point heldPoint))
        {
            MoveCursor(heldPoint);
        }
        else
        {
            MoveCursor(screenPoint);
        }
        if (!_readyLogged)
        {
            Log(
                "controller-pointer-input-ready",
                $"The configured pointer hand is mapped to the game client area;triggerClick={_settings.TriggerClickEnabled};thumbstickScroll={_settings.ThumbstickScrollEnabled};requireFocus={_settings.RequireGameFocus}.");
            _readyLogged = true;
        }

        if (primaryRising && _primarySource == PrimarySource.None)
        {
            BeginPrimary(PrimarySource.PrimaryButton, screenPoint);
        }
        else if (controllerFrame.PointerPrimaryPressed &&
            _primarySource == PrimarySource.PrimaryButton)
        {
            MoveCursor(screenPoint);
        }
        if (primaryFalling && _primarySource == PrimarySource.PrimaryButton)
        {
            EndPrimary(screenPoint);
        }

        if (_suppressTriggerUntilReleased)
        {
            if (triggerValue <= 0.10f)
            {
                _suppressTriggerUntilReleased = false;
            }
        }
        AnalogPressTransition triggerTransition = _suppressTriggerUntilReleased
            ? AnalogPressTransition.None
            : _triggerLatch.Update(active: _settings.TriggerClickEnabled, triggerValue);
        if (triggerTransition == AnalogPressTransition.Armed)
        {
            _latchedU = u;
            _latchedV = v;
            _triggerCoordinateLatched = true;
            _triggerDragging = false;
        }
        else if (triggerTransition == AnalogPressTransition.Pressed &&
            _primarySource == PrimarySource.None)
        {
            if (!_triggerCoordinateLatched)
            {
                _latchedU = u;
                _latchedV = v;
                _triggerCoordinateLatched = true;
            }

            if (TryResolveForegroundClientPoint(_latchedU, _latchedV, out Point latchedPoint))
            {
                BeginPrimary(PrimarySource.Trigger, latchedPoint);
                _triggerPressedTimestamp = Stopwatch.GetTimestamp();
            }
        }
        else if (triggerTransition == AnalogPressTransition.Released)
        {
            if (_primarySource == PrimarySource.Trigger)
            {
                Point releasePoint = screenPoint;
                if (!_triggerDragging &&
                    TryResolveForegroundClientPoint(_latchedU, _latchedV, out Point latchedPoint))
                {
                    releasePoint = latchedPoint;
                }
                EndPrimary(releasePoint);
            }
            _triggerCoordinateLatched = false;
            _triggerDragging = false;
        }

        if (_primarySource == PrimarySource.Trigger && _triggerLatch.IsPressed)
        {
            long now = Stopwatch.GetTimestamp();
            float deltaU = u - _latchedU;
            float deltaV = v - _latchedV;
            if (!_triggerDragging && now - _triggerPressedTimestamp >= TriggerDragDelayTicks &&
                (deltaU * deltaU) + (deltaV * deltaV) >= TriggerDragDistanceSquared)
            {
                _triggerDragging = true;
            }
            if (_triggerDragging)
            {
                MoveCursor(screenPoint);
            }
        }

        if (secondaryRising)
        {
            NativeMethods.keybd_event(VirtualKeyEscape, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(VirtualKeyEscape, 0, KeyUp, UIntPtr.Zero);
        }

        long scrollNow = Stopwatch.GetTimestamp();
        if (_settings.ThumbstickScrollEnabled &&
            MathF.Abs(controllerFrame.PointerThumbstickY) >= 0.55f &&
            scrollNow >= _nextScrollTimestamp)
        {
            int wheelDelta = (int)MathF.Round(
                (controllerFrame.PointerThumbstickY > 0f ? 120f : -120f) *
                _settings.ScrollSensitivity);
            NativeMethods.mouse_event(
                MouseWheel,
                0,
                0,
                unchecked((uint)wheelDelta),
                UIntPtr.Zero);
            _nextScrollTimestamp = scrollNow + ScrollRepeatTicks;
        }

        bool visualLocked = _triggerCoordinateLatched &&
            (_triggerLatch.IsArmed || _primarySource == PrimarySource.Trigger) &&
            !_triggerDragging;
        return new VrPointerVisual(
            true,
            visualLocked ? _latchedU : u,
            visualLocked ? _latchedV : v);
    }

    public void Dispose()
    {
        ReleasePrimaryIfNeeded();
        _ = _triggerLatch.Cancel();
    }

    private void BeginPrimary(PrimarySource source, Point point)
    {
        MoveCursor(point);
        NativeMethods.mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
        _primarySource = source;
    }

    private void EndPrimary(Point point)
    {
        MoveCursor(point);
        NativeMethods.mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
        _primarySource = PrimarySource.None;
    }

    private void ReleasePrimaryIfNeeded()
    {
        if (_primarySource == PrimarySource.None)
        {
            return;
        }

        NativeMethods.mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
        _primarySource = PrimarySource.None;
    }

    private bool TryResolveForegroundClientPoint(float u, float v, out Point screenPoint)
    {
        screenPoint = default;
        IntPtr window = _gameWindow;
        if (window == IntPtr.Zero || !NativeMethods.IsWindow(window))
        {
            using Process process = Process.GetCurrentProcess();
            window = process.MainWindowHandle;
            _gameWindow = window;
        }
        if (window == IntPtr.Zero ||
            (_settings.RequireGameFocus && NativeMethods.GetForegroundWindow() != window))
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now >= _nextBlockedLogUtc)
            {
                Log(
                    "controller-pointer-input-blocked",
                    "Controller input was not injected because the game window is not the foreground window.");
                _nextBlockedLogUtc = now.AddSeconds(10);
            }
            return false;
        }

        if (!NativeMethods.GetClientRect(window, out Rect clientRect))
        {
            return false;
        }

        int width = clientRect.Right - clientRect.Left;
        int height = clientRect.Bottom - clientRect.Top;
        if (width <= 1 || height <= 1)
        {
            return false;
        }

        Point clientPoint = new()
        {
            X = Math.Clamp((int)MathF.Round(u * (width - 1)), 0, width - 1),
            Y = Math.Clamp((int)MathF.Round(v * (height - 1)), 0, height - 1)
        };
        if (!NativeMethods.ClientToScreen(window, ref clientPoint))
        {
            return false;
        }

        screenPoint = clientPoint;
        return true;
    }

    private static void MoveCursor(Point point)
    {
        _ = NativeMethods.SetCursorPos(point.X, point.Y);
    }

    private static void Log(string eventName, string reason)
    {
        RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Event = eventName,
            BootstrapVersion = RuntimeProbe.BootstrapVersion,
            ProcessId = Environment.ProcessId,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Reason = reason
        });
    }

    private enum PrimarySource
    {
        None,
        PrimaryButton,
        Trigger
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr window, ref Point point);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(
            uint flags,
            uint dx,
            uint dy,
            uint data,
            UIntPtr extraInfo);

        [DllImport("user32.dll")]
        public static extern void keybd_event(
            byte virtualKey,
            byte scanCode,
            uint flags,
            UIntPtr extraInfo);
    }
}
