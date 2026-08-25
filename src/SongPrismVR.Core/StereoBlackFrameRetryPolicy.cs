namespace SongPrismVR.Core;

public enum StereoBlackFrameDecision
{
    Retry,
    TimedOut
}

public sealed class StereoBlackFrameRetryPolicy
{
    private readonly int _maximumAttempts;
    private readonly long _timeoutMilliseconds;
    private int _attemptCount;
    private long _firstAttemptMilliseconds;

    public StereoBlackFrameRetryPolicy(
        int maximumAttempts = 20,
        long timeoutMilliseconds = 2_000)
    {
        if (maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }
        if (timeoutMilliseconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        }
        _maximumAttempts = maximumAttempts;
        _timeoutMilliseconds = timeoutMilliseconds;
    }

    public int AttemptCount => _attemptCount;

    public StereoBlackFrameDecision ObserveBlack(long nowMilliseconds)
    {
        if (_attemptCount == 0)
        {
            _firstAttemptMilliseconds = nowMilliseconds;
        }
        _attemptCount++;
        return _attemptCount >= _maximumAttempts ||
            nowMilliseconds - _firstAttemptMilliseconds >= _timeoutMilliseconds
            ? StereoBlackFrameDecision.TimedOut
            : StereoBlackFrameDecision.Retry;
    }

    public void Reset()
    {
        _attemptCount = 0;
        _firstAttemptMilliseconds = 0;
    }
}
