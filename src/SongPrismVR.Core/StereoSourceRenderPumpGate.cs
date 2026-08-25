namespace SongPrismVR.Core;

public sealed class StereoSourceRenderPumpGate
{
    private long _sourceToken;
    private int _sourceObservedFrame = int.MinValue;
    private int _claimedFrame = int.MinValue;

    public void SetSource(long sourceToken, int observedFrame)
    {
        if (_sourceToken == sourceToken)
        {
            return;
        }

        _sourceToken = sourceToken;
        _sourceObservedFrame = observedFrame;
        _claimedFrame = int.MinValue;
    }

    public bool TryClaim(int frameCount)
    {
        if (_sourceToken == 0 || frameCount == _sourceObservedFrame ||
            frameCount == _claimedFrame)
        {
            return false;
        }

        _claimedFrame = frameCount;
        return true;
    }

    public void Reset()
    {
        _sourceToken = 0;
        _sourceObservedFrame = int.MinValue;
        _claimedFrame = int.MinValue;
    }
}
