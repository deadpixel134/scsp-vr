namespace SongPrismVR.Core;

public sealed class GripToggleLatch
{
    private readonly float _pressThreshold;
    private readonly float _releaseThreshold;
    private readonly long _debounceTicks;
    private bool _released = true;
    private long _nextToggleTimestamp;

    public GripToggleLatch(
        float pressThreshold,
        float releaseThreshold,
        long debounceTicks,
        bool initialEnabled = false)
    {
        if (pressThreshold <= releaseThreshold || pressThreshold > 1f || releaseThreshold < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(pressThreshold));
        }
        if (debounceTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceTicks));
        }
        _pressThreshold = pressThreshold;
        _releaseThreshold = releaseThreshold;
        _debounceTicks = debounceTicks;
        Enabled = initialEnabled;
    }

    public bool Enabled { get; private set; }

    public bool Update(bool active, float value, long timestamp)
    {
        if (!active)
        {
            _released = true;
            return false;
        }
        if (value <= _releaseThreshold)
        {
            _released = true;
            return false;
        }
        if (!_released || value < _pressThreshold)
        {
            return false;
        }

        _released = false;
        if (timestamp < _nextToggleTimestamp)
        {
            return false;
        }
        Enabled = !Enabled;
        _nextToggleTimestamp = timestamp + _debounceTicks;
        return true;
    }
}
