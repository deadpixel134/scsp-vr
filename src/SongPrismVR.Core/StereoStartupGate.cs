namespace SongPrismVR.Core;

public sealed class StereoStartupGate
{
    private readonly int _requiredStableFrames;
    private int _setupFrameCount;
    private long _setupPresentSerial;
    private bool _armed;

    public StereoStartupGate(int requiredStableFrames = 2)
    {
        if (requiredStableFrames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredStableFrames));
        }

        _requiredStableFrames = requiredStableFrames;
    }

    public void Arm(int frameCount, long presentSerial)
    {
        _setupFrameCount = frameCount;
        _setupPresentSerial = presentSerial;
        _armed = true;
    }

    public bool IsReady(int frameCount, long presentSerial)
    {
        if (!_armed || presentSerial <= _setupPresentSerial)
        {
            return false;
        }

        return (long)frameCount - _setupFrameCount >= _requiredStableFrames;
    }

    public string Describe()
    {
        return $"armed={_armed};setupFrameCount={_setupFrameCount};setupPresentSerial={_setupPresentSerial};requiredStableFrames={_requiredStableFrames}";
    }

    public void Reset()
    {
        _setupFrameCount = 0;
        _setupPresentSerial = 0;
        _armed = false;
    }
}
