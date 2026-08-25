namespace SongPrismVR.Core;

public enum AnalogPressTransition
{
    None,
    Armed,
    Pressed,
    Released
}

public sealed class AnalogPressLatch
{
    private readonly float _armThreshold;
    private readonly float _pressThreshold;
    private readonly float _cancelThreshold;
    private readonly float _releaseThreshold;
    private bool _armed;
    private bool _pressed;

    public AnalogPressLatch(
        float armThreshold,
        float pressThreshold,
        float cancelThreshold,
        float releaseThreshold)
    {
        if (cancelThreshold < 0f || armThreshold <= cancelThreshold ||
            releaseThreshold < armThreshold || pressThreshold <= releaseThreshold ||
            pressThreshold > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(armThreshold));
        }

        _armThreshold = armThreshold;
        _pressThreshold = pressThreshold;
        _cancelThreshold = cancelThreshold;
        _releaseThreshold = releaseThreshold;
    }

    public bool IsArmed => _armed;

    public bool IsPressed => _pressed;

    public AnalogPressTransition Update(bool active, float value)
    {
        if (!active || (_pressed && value <= _releaseThreshold) ||
            (!_pressed && value <= _cancelThreshold))
        {
            bool wasPressed = _pressed;
            _armed = false;
            _pressed = false;
            return wasPressed ? AnalogPressTransition.Released : AnalogPressTransition.None;
        }

        if (!_armed && !_pressed && value >= _armThreshold)
        {
            _armed = true;
            if (value < _pressThreshold)
            {
                return AnalogPressTransition.Armed;
            }
        }

        if (_armed && !_pressed && value >= _pressThreshold)
        {
            _pressed = true;
            return AnalogPressTransition.Pressed;
        }

        return AnalogPressTransition.None;
    }

    public bool Cancel()
    {
        bool wasPressed = _pressed;
        _armed = false;
        _pressed = false;
        return wasPressed;
    }
}
