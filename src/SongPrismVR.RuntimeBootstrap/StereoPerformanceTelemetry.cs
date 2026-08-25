using System.Diagnostics;

namespace Doorstop;

internal sealed class StereoPerformanceProbeRecord
{
    public double WindowMilliseconds { get; init; }
    public long PresentCount { get; init; }
    public double PresentFramesPerSecond { get; init; }
    public long OpenXrFrameCount { get; init; }
    public double OpenXrFramesPerSecond { get; init; }
    public double? OpenXrPredictedDisplayHz { get; init; }
    public long StereoArmCount { get; init; }
    public long StereoArmBeforeSourceRenderCount { get; init; }
    public long StereoArmAfterSourceRenderCount { get; init; }
    public long StereoWaitNoPresentBoundaryCount { get; init; }
    public long StereoWaitBothCloneRendersMissingCount { get; init; }
    public long StereoWaitLeftCloneRenderMissingCount { get; init; }
    public long StereoWaitRightCloneRenderMissingCount { get; init; }
    public long StereoBufferReuseBlockedCount { get; init; }
    public long StereoPairRenderCompletionPresentDeltaZero { get; init; }
    public long StereoPairRenderCompletionPresentDeltaOne { get; init; }
    public long StereoPairRenderCompletionPresentDeltaTwoOrMore { get; init; }
    public long StereoFinalizeCount { get; init; }
    public long StereoPublishCount { get; init; }
    public double StereoPublishesPerSecond { get; init; }
    public double? StereoPublishIntervalAverageMilliseconds { get; init; }
    public double? StereoPublishIntervalMaximumMilliseconds { get; init; }
    public long StereoPublishIntervalAtMost8Milliseconds { get; init; }
    public long StereoPublishIntervalAtMost17Milliseconds { get; init; }
    public long StereoPublishIntervalAtMost25Milliseconds { get; init; }
    public long StereoPublishIntervalAtMost34Milliseconds { get; init; }
    public long StereoPublishIntervalAtMost50Milliseconds { get; init; }
    public long StereoPublishIntervalAtMost100Milliseconds { get; init; }
    public long StereoPublishIntervalOver100Milliseconds { get; init; }
    public long StereoPresentDeltaZero { get; init; }
    public long StereoPresentDeltaOne { get; init; }
    public long StereoPresentDeltaTwo { get; init; }
    public long StereoPresentDeltaThree { get; init; }
    public long StereoPresentDeltaFourOrMore { get; init; }
    public double? StereoArmToFinalizeAverageMilliseconds { get; init; }
    public double? StereoArmToFinalizeMaximumMilliseconds { get; init; }
    public double? StereoGpuWaitAverageMilliseconds { get; init; }
    public double? StereoGpuWaitMaximumMilliseconds { get; init; }
    public double? StereoFinalizeAverageMilliseconds { get; init; }
    public double? StereoFinalizeMaximumMilliseconds { get; init; }
    public long SourceVisualEffectRenderCount { get; init; }
    public long CloneVisualEffectRenderCount { get; init; }
    public long SourceToCloneRenderSampleCount { get; init; }
    public double? SourceToCloneRenderAverageMilliseconds { get; init; }
    public double? SourceToCloneRenderMaximumMilliseconds { get; init; }
    public long SourceToCloneSamePresentCount { get; init; }
    public long SourceToCloneOnePresentLaterCount { get; init; }
    public long SourceToCloneTwoOrMorePresentsLaterCount { get; init; }
    public long OpenXrStereoSubmitCount { get; init; }
    public double? OpenXrPairAgeAverageMilliseconds { get; init; }
    public double? OpenXrPairAgeMaximumMilliseconds { get; init; }
    public double? OpenXrStereoCopyAverageMilliseconds { get; init; }
    public double? OpenXrStereoCopyMaximumMilliseconds { get; init; }
    public double? OpenXrStereoGpuWaitAverageMilliseconds { get; init; }
    public double? OpenXrStereoGpuWaitMaximumMilliseconds { get; init; }
    public double? OpenXrEndFrameAverageMilliseconds { get; init; }
    public double? OpenXrEndFrameMaximumMilliseconds { get; init; }
}

internal static class StereoPerformanceTelemetry
{
    private static readonly object SnapshotLock = new();
    private static readonly long TicksAt8Milliseconds = MillisecondsToTicks(8.333333);
    private static readonly long TicksAt17Milliseconds = MillisecondsToTicks(16.666667);
    private static readonly long TicksAt25Milliseconds = MillisecondsToTicks(25.0);
    private static readonly long TicksAt34Milliseconds = MillisecondsToTicks(33.333333);
    private static readonly long TicksAt50Milliseconds = MillisecondsToTicks(50.0);
    private static readonly long TicksAt100Milliseconds = MillisecondsToTicks(100.0);
    private static long _windowStartedTimestamp = Stopwatch.GetTimestamp();
    private static long _presentCount;
    private static long _openXrFrameCount;
    private static long _predictedDisplayPeriodCount;
    private static long _predictedDisplayPeriodNanosecondsTotal;
    private static long _stereoArmCount;
    private static long _stereoArmBeforeSourceRenderCount;
    private static long _stereoArmAfterSourceRenderCount;
    private static long _stereoWaitNoPresentBoundaryCount;
    private static long _stereoWaitBothCloneRendersMissingCount;
    private static long _stereoWaitLeftCloneRenderMissingCount;
    private static long _stereoWaitRightCloneRenderMissingCount;
    private static long _stereoBufferReuseBlockedCount;
    private static long _stereoPairRenderCompletionPresentDeltaZero;
    private static long _stereoPairRenderCompletionPresentDeltaOne;
    private static long _stereoPairRenderCompletionPresentDeltaTwoOrMore;
    private static long _stereoFinalizeCount;
    private static long _stereoPublishCount;
    private static long _lastStereoPublishTimestamp;
    private static long _stereoPublishIntervalCount;
    private static long _stereoPublishIntervalTicksTotal;
    private static long _stereoPublishIntervalTicksMaximum;
    private static long _publishIntervalAtMost8Milliseconds;
    private static long _publishIntervalAtMost17Milliseconds;
    private static long _publishIntervalAtMost25Milliseconds;
    private static long _publishIntervalAtMost34Milliseconds;
    private static long _publishIntervalAtMost50Milliseconds;
    private static long _publishIntervalAtMost100Milliseconds;
    private static long _publishIntervalOver100Milliseconds;
    private static long _presentDeltaZero;
    private static long _presentDeltaOne;
    private static long _presentDeltaTwo;
    private static long _presentDeltaThree;
    private static long _presentDeltaFourOrMore;
    private static long _armToFinalizeCount;
    private static long _armToFinalizeTicksTotal;
    private static long _armToFinalizeTicksMaximum;
    private static long _gpuWaitCount;
    private static long _gpuWaitTicksTotal;
    private static long _gpuWaitTicksMaximum;
    private static long _finalizeDurationCount;
    private static long _finalizeDurationTicksTotal;
    private static long _finalizeDurationTicksMaximum;
    private static long _sourceVisualEffectRenderCount;
    private static long _cloneVisualEffectRenderCount;
    private static long _latestSourceVisualEffectTimestamp;
    private static long _latestSourceVisualEffectPresentSerial;
    private static long _sourceToCloneRenderCount;
    private static long _sourceToCloneRenderTicksTotal;
    private static long _sourceToCloneRenderTicksMaximum;
    private static long _sourceToCloneSamePresentCount;
    private static long _sourceToCloneOnePresentLaterCount;
    private static long _sourceToCloneTwoOrMorePresentsLaterCount;
    private static long _openXrStereoSubmitCount;
    private static long _openXrPairAgeCount;
    private static long _openXrPairAgeTicksTotal;
    private static long _openXrPairAgeTicksMaximum;
    private static long _openXrStereoCopyCount;
    private static long _openXrStereoCopyTicksTotal;
    private static long _openXrStereoCopyTicksMaximum;
    private static long _openXrStereoGpuWaitCount;
    private static long _openXrStereoGpuWaitTicksTotal;
    private static long _openXrStereoGpuWaitTicksMaximum;
    private static long _openXrEndFrameCount;
    private static long _openXrEndFrameTicksTotal;
    private static long _openXrEndFrameTicksMaximum;

    public static void RecordPresent() => Interlocked.Increment(ref _presentCount);

    public static void RecordOpenXrFrame(long predictedDisplayPeriodNanoseconds)
    {
        Interlocked.Increment(ref _openXrFrameCount);
        if (predictedDisplayPeriodNanoseconds > 0)
        {
            Interlocked.Increment(ref _predictedDisplayPeriodCount);
            Interlocked.Add(
                ref _predictedDisplayPeriodNanosecondsTotal,
                predictedDisplayPeriodNanoseconds);
        }
    }

    public static void RecordStereoArm(bool sourceAlreadyRenderedInPresent)
    {
        Interlocked.Increment(ref _stereoArmCount);
        if (sourceAlreadyRenderedInPresent)
        {
            Interlocked.Increment(ref _stereoArmAfterSourceRenderCount);
        }
        else
        {
            Interlocked.Increment(ref _stereoArmBeforeSourceRenderCount);
        }
    }

    public static void RecordStereoWait(
        bool presentBoundaryReady,
        int cloneRenderCompletionMask)
    {
        if (!presentBoundaryReady)
        {
            Interlocked.Increment(ref _stereoWaitNoPresentBoundaryCount);
            return;
        }

        if (cloneRenderCompletionMask == 0)
        {
            Interlocked.Increment(ref _stereoWaitBothCloneRendersMissingCount);
        }
        else if ((cloneRenderCompletionMask & 1) == 0)
        {
            Interlocked.Increment(ref _stereoWaitLeftCloneRenderMissingCount);
        }
        else if ((cloneRenderCompletionMask & 2) == 0)
        {
            Interlocked.Increment(ref _stereoWaitRightCloneRenderMissingCount);
        }
    }

    public static void RecordStereoPairRenderCompletion(long presentDelta)
    {
        if (presentDelta <= 0)
        {
            Interlocked.Increment(ref _stereoPairRenderCompletionPresentDeltaZero);
        }
        else if (presentDelta == 1)
        {
            Interlocked.Increment(ref _stereoPairRenderCompletionPresentDeltaOne);
        }
        else
        {
            Interlocked.Increment(ref _stereoPairRenderCompletionPresentDeltaTwoOrMore);
        }
    }

    public static void RecordStereoBufferReuseBlocked() =>
        Interlocked.Increment(ref _stereoBufferReuseBlockedCount);

    public static void RecordStereoFinalize(
        long armTimestamp,
        long presentDelta,
        long gpuWaitTicks,
        long finalizeDurationTicks)
    {
        Interlocked.Increment(ref _stereoFinalizeCount);
        if (armTimestamp > 0)
        {
            RecordDuration(
                Stopwatch.GetTimestamp() - armTimestamp,
                ref _armToFinalizeCount,
                ref _armToFinalizeTicksTotal,
                ref _armToFinalizeTicksMaximum);
        }

        RecordDuration(
            gpuWaitTicks,
            ref _gpuWaitCount,
            ref _gpuWaitTicksTotal,
            ref _gpuWaitTicksMaximum);
        RecordDuration(
            finalizeDurationTicks,
            ref _finalizeDurationCount,
            ref _finalizeDurationTicksTotal,
            ref _finalizeDurationTicksMaximum);

        if (presentDelta <= 0)
        {
            Interlocked.Increment(ref _presentDeltaZero);
        }
        else if (presentDelta == 1)
        {
            Interlocked.Increment(ref _presentDeltaOne);
        }
        else if (presentDelta == 2)
        {
            Interlocked.Increment(ref _presentDeltaTwo);
        }
        else if (presentDelta == 3)
        {
            Interlocked.Increment(ref _presentDeltaThree);
        }
        else
        {
            Interlocked.Increment(ref _presentDeltaFourOrMore);
        }
    }

    public static void RecordStereoPublish()
    {
        long now = Stopwatch.GetTimestamp();
        long previous = Interlocked.Exchange(ref _lastStereoPublishTimestamp, now);
        Interlocked.Increment(ref _stereoPublishCount);
        if (previous <= 0 || now <= previous)
        {
            return;
        }

        long interval = now - previous;
        RecordDuration(
            interval,
            ref _stereoPublishIntervalCount,
            ref _stereoPublishIntervalTicksTotal,
            ref _stereoPublishIntervalTicksMaximum);
        if (interval <= TicksAt8Milliseconds)
        {
            Interlocked.Increment(ref _publishIntervalAtMost8Milliseconds);
        }
        else if (interval <= TicksAt17Milliseconds)
        {
            Interlocked.Increment(ref _publishIntervalAtMost17Milliseconds);
        }
        else if (interval <= TicksAt25Milliseconds)
        {
            Interlocked.Increment(ref _publishIntervalAtMost25Milliseconds);
        }
        else if (interval <= TicksAt34Milliseconds)
        {
            Interlocked.Increment(ref _publishIntervalAtMost34Milliseconds);
        }
        else if (interval <= TicksAt50Milliseconds)
        {
            Interlocked.Increment(ref _publishIntervalAtMost50Milliseconds);
        }
        else if (interval <= TicksAt100Milliseconds)
        {
            Interlocked.Increment(ref _publishIntervalAtMost100Milliseconds);
        }
        else
        {
            Interlocked.Increment(ref _publishIntervalOver100Milliseconds);
        }
    }

    public static void RecordSourceVisualEffectRender(long presentSerial)
    {
        Interlocked.Increment(ref _sourceVisualEffectRenderCount);
        Volatile.Write(ref _latestSourceVisualEffectPresentSerial, presentSerial);
        Volatile.Write(ref _latestSourceVisualEffectTimestamp, Stopwatch.GetTimestamp());
    }

    public static void RecordCloneVisualEffectRender(long presentSerial)
    {
        Interlocked.Increment(ref _cloneVisualEffectRenderCount);
        long sourceTimestamp = Volatile.Read(ref _latestSourceVisualEffectTimestamp);
        long sourcePresentSerial = Volatile.Read(ref _latestSourceVisualEffectPresentSerial);
        long now = Stopwatch.GetTimestamp();
        if (sourceTimestamp <= 0 || now < sourceTimestamp || presentSerial < sourcePresentSerial)
        {
            return;
        }

        RecordDuration(
            now - sourceTimestamp,
            ref _sourceToCloneRenderCount,
            ref _sourceToCloneRenderTicksTotal,
            ref _sourceToCloneRenderTicksMaximum);
        long presentDelta = presentSerial - sourcePresentSerial;
        if (presentDelta == 0)
        {
            Interlocked.Increment(ref _sourceToCloneSamePresentCount);
        }
        else if (presentDelta == 1)
        {
            Interlocked.Increment(ref _sourceToCloneOnePresentLaterCount);
        }
        else
        {
            Interlocked.Increment(ref _sourceToCloneTwoOrMorePresentsLaterCount);
        }
    }

    public static void RecordOpenXrStereoSubmission(
        long publishedTimestamp,
        long stereoCopyDurationTicks,
        long stereoGpuWaitDurationTicks)
    {
        Interlocked.Increment(ref _openXrStereoSubmitCount);
        RecordDuration(
            stereoCopyDurationTicks,
            ref _openXrStereoCopyCount,
            ref _openXrStereoCopyTicksTotal,
            ref _openXrStereoCopyTicksMaximum);
        RecordDuration(
            stereoGpuWaitDurationTicks,
            ref _openXrStereoGpuWaitCount,
            ref _openXrStereoGpuWaitTicksTotal,
            ref _openXrStereoGpuWaitTicksMaximum);
        long now = Stopwatch.GetTimestamp();
        if (publishedTimestamp > 0 && now >= publishedTimestamp)
        {
            RecordDuration(
                now - publishedTimestamp,
                ref _openXrPairAgeCount,
                ref _openXrPairAgeTicksTotal,
                ref _openXrPairAgeTicksMaximum);
        }
    }

    public static void RecordOpenXrEndFrame(long durationTicks) =>
        RecordDuration(
            durationTicks,
            ref _openXrEndFrameCount,
            ref _openXrEndFrameTicksTotal,
            ref _openXrEndFrameTicksMaximum);

    public static StereoPerformanceProbeRecord? SnapshotAndReset()
    {
        lock (SnapshotLock)
        {
            long now = Stopwatch.GetTimestamp();
            long started = Volatile.Read(ref _windowStartedTimestamp);
            long elapsedTicks = now - started;
            if (elapsedTicks < Stopwatch.Frequency)
            {
                return null;
            }

            Volatile.Write(ref _windowStartedTimestamp, now);
            long presentCount = Exchange(ref _presentCount);
            long openXrFrameCount = Exchange(ref _openXrFrameCount);
            long predictedPeriodCount = Exchange(ref _predictedDisplayPeriodCount);
            long predictedPeriodTotal = Exchange(ref _predictedDisplayPeriodNanosecondsTotal);
            long stereoArmCount = Exchange(ref _stereoArmCount);
            long stereoFinalizeCount = Exchange(ref _stereoFinalizeCount);
            long stereoPublishCount = Exchange(ref _stereoPublishCount);
            long publishIntervalCount = Exchange(ref _stereoPublishIntervalCount);
            long publishIntervalTotal = Exchange(ref _stereoPublishIntervalTicksTotal);
            long publishIntervalMaximum = Exchange(ref _stereoPublishIntervalTicksMaximum);
            long armToFinalizeCount = Exchange(ref _armToFinalizeCount);
            long armToFinalizeTotal = Exchange(ref _armToFinalizeTicksTotal);
            long armToFinalizeMaximum = Exchange(ref _armToFinalizeTicksMaximum);
            long gpuWaitCount = Exchange(ref _gpuWaitCount);
            long gpuWaitTotal = Exchange(ref _gpuWaitTicksTotal);
            long gpuWaitMaximum = Exchange(ref _gpuWaitTicksMaximum);
            long finalizeCount = Exchange(ref _finalizeDurationCount);
            long finalizeTotal = Exchange(ref _finalizeDurationTicksTotal);
            long finalizeMaximum = Exchange(ref _finalizeDurationTicksMaximum);
            long sourceToCloneCount = Exchange(ref _sourceToCloneRenderCount);
            long sourceToCloneTotal = Exchange(ref _sourceToCloneRenderTicksTotal);
            long sourceToCloneMaximum = Exchange(ref _sourceToCloneRenderTicksMaximum);
            long pairAgeCount = Exchange(ref _openXrPairAgeCount);
            long pairAgeTotal = Exchange(ref _openXrPairAgeTicksTotal);
            long pairAgeMaximum = Exchange(ref _openXrPairAgeTicksMaximum);
            long copyCount = Exchange(ref _openXrStereoCopyCount);
            long copyTotal = Exchange(ref _openXrStereoCopyTicksTotal);
            long copyMaximum = Exchange(ref _openXrStereoCopyTicksMaximum);
            long stereoGpuWaitCount = Exchange(ref _openXrStereoGpuWaitCount);
            long stereoGpuWaitTotal = Exchange(ref _openXrStereoGpuWaitTicksTotal);
            long stereoGpuWaitMaximum = Exchange(ref _openXrStereoGpuWaitTicksMaximum);
            long endFrameCount = Exchange(ref _openXrEndFrameCount);
            long endFrameTotal = Exchange(ref _openXrEndFrameTicksTotal);
            long endFrameMaximum = Exchange(ref _openXrEndFrameTicksMaximum);
            double elapsedSeconds = elapsedTicks / (double)Stopwatch.Frequency;
            double? predictedDisplayHz = predictedPeriodCount > 0 && predictedPeriodTotal > 0
                ? 1_000_000_000.0 / (predictedPeriodTotal / (double)predictedPeriodCount)
                : null;

            return new StereoPerformanceProbeRecord
            {
                WindowMilliseconds = TicksToMilliseconds(elapsedTicks),
                PresentCount = presentCount,
                PresentFramesPerSecond = presentCount / elapsedSeconds,
                OpenXrFrameCount = openXrFrameCount,
                OpenXrFramesPerSecond = openXrFrameCount / elapsedSeconds,
                OpenXrPredictedDisplayHz = predictedDisplayHz,
                StereoArmCount = stereoArmCount,
                StereoArmBeforeSourceRenderCount =
                    Exchange(ref _stereoArmBeforeSourceRenderCount),
                StereoArmAfterSourceRenderCount =
                    Exchange(ref _stereoArmAfterSourceRenderCount),
                StereoWaitNoPresentBoundaryCount =
                    Exchange(ref _stereoWaitNoPresentBoundaryCount),
                StereoWaitBothCloneRendersMissingCount =
                    Exchange(ref _stereoWaitBothCloneRendersMissingCount),
                StereoWaitLeftCloneRenderMissingCount =
                    Exchange(ref _stereoWaitLeftCloneRenderMissingCount),
                StereoWaitRightCloneRenderMissingCount =
                    Exchange(ref _stereoWaitRightCloneRenderMissingCount),
                StereoBufferReuseBlockedCount =
                    Exchange(ref _stereoBufferReuseBlockedCount),
                StereoPairRenderCompletionPresentDeltaZero =
                    Exchange(ref _stereoPairRenderCompletionPresentDeltaZero),
                StereoPairRenderCompletionPresentDeltaOne =
                    Exchange(ref _stereoPairRenderCompletionPresentDeltaOne),
                StereoPairRenderCompletionPresentDeltaTwoOrMore =
                    Exchange(ref _stereoPairRenderCompletionPresentDeltaTwoOrMore),
                StereoFinalizeCount = stereoFinalizeCount,
                StereoPublishCount = stereoPublishCount,
                StereoPublishesPerSecond = stereoPublishCount / elapsedSeconds,
                StereoPublishIntervalAverageMilliseconds = AverageMilliseconds(
                    publishIntervalTotal,
                    publishIntervalCount),
                StereoPublishIntervalMaximumMilliseconds = MaximumMilliseconds(
                    publishIntervalMaximum,
                    publishIntervalCount),
                StereoPublishIntervalAtMost8Milliseconds =
                    Exchange(ref _publishIntervalAtMost8Milliseconds),
                StereoPublishIntervalAtMost17Milliseconds =
                    Exchange(ref _publishIntervalAtMost17Milliseconds),
                StereoPublishIntervalAtMost25Milliseconds =
                    Exchange(ref _publishIntervalAtMost25Milliseconds),
                StereoPublishIntervalAtMost34Milliseconds =
                    Exchange(ref _publishIntervalAtMost34Milliseconds),
                StereoPublishIntervalAtMost50Milliseconds =
                    Exchange(ref _publishIntervalAtMost50Milliseconds),
                StereoPublishIntervalAtMost100Milliseconds =
                    Exchange(ref _publishIntervalAtMost100Milliseconds),
                StereoPublishIntervalOver100Milliseconds =
                    Exchange(ref _publishIntervalOver100Milliseconds),
                StereoPresentDeltaZero = Exchange(ref _presentDeltaZero),
                StereoPresentDeltaOne = Exchange(ref _presentDeltaOne),
                StereoPresentDeltaTwo = Exchange(ref _presentDeltaTwo),
                StereoPresentDeltaThree = Exchange(ref _presentDeltaThree),
                StereoPresentDeltaFourOrMore = Exchange(ref _presentDeltaFourOrMore),
                StereoArmToFinalizeAverageMilliseconds = AverageMilliseconds(
                    armToFinalizeTotal,
                    armToFinalizeCount),
                StereoArmToFinalizeMaximumMilliseconds = MaximumMilliseconds(
                    armToFinalizeMaximum,
                    armToFinalizeCount),
                StereoGpuWaitAverageMilliseconds = AverageMilliseconds(gpuWaitTotal, gpuWaitCount),
                StereoGpuWaitMaximumMilliseconds = MaximumMilliseconds(gpuWaitMaximum, gpuWaitCount),
                StereoFinalizeAverageMilliseconds = AverageMilliseconds(finalizeTotal, finalizeCount),
                StereoFinalizeMaximumMilliseconds = MaximumMilliseconds(finalizeMaximum, finalizeCount),
                SourceVisualEffectRenderCount = Exchange(ref _sourceVisualEffectRenderCount),
                CloneVisualEffectRenderCount = Exchange(ref _cloneVisualEffectRenderCount),
                SourceToCloneRenderSampleCount = sourceToCloneCount,
                SourceToCloneRenderAverageMilliseconds = AverageMilliseconds(
                    sourceToCloneTotal,
                    sourceToCloneCount),
                SourceToCloneRenderMaximumMilliseconds = MaximumMilliseconds(
                    sourceToCloneMaximum,
                    sourceToCloneCount),
                SourceToCloneSamePresentCount = Exchange(ref _sourceToCloneSamePresentCount),
                SourceToCloneOnePresentLaterCount =
                    Exchange(ref _sourceToCloneOnePresentLaterCount),
                SourceToCloneTwoOrMorePresentsLaterCount =
                    Exchange(ref _sourceToCloneTwoOrMorePresentsLaterCount),
                OpenXrStereoSubmitCount = Exchange(ref _openXrStereoSubmitCount),
                OpenXrPairAgeAverageMilliseconds = AverageMilliseconds(pairAgeTotal, pairAgeCount),
                OpenXrPairAgeMaximumMilliseconds = MaximumMilliseconds(pairAgeMaximum, pairAgeCount),
                OpenXrStereoCopyAverageMilliseconds = AverageMilliseconds(copyTotal, copyCount),
                OpenXrStereoCopyMaximumMilliseconds = MaximumMilliseconds(copyMaximum, copyCount),
                OpenXrStereoGpuWaitAverageMilliseconds = AverageMilliseconds(
                    stereoGpuWaitTotal,
                    stereoGpuWaitCount),
                OpenXrStereoGpuWaitMaximumMilliseconds = MaximumMilliseconds(
                    stereoGpuWaitMaximum,
                    stereoGpuWaitCount),
                OpenXrEndFrameAverageMilliseconds = AverageMilliseconds(endFrameTotal, endFrameCount),
                OpenXrEndFrameMaximumMilliseconds = MaximumMilliseconds(endFrameMaximum, endFrameCount)
            };
        }
    }

    private static void RecordDuration(
        long durationTicks,
        ref long count,
        ref long total,
        ref long maximum)
    {
        if (durationTicks < 0)
        {
            return;
        }

        Interlocked.Increment(ref count);
        Interlocked.Add(ref total, durationTicks);
        UpdateMaximum(ref maximum, durationTicks);
    }

    private static void UpdateMaximum(ref long target, long value)
    {
        long observed = Volatile.Read(ref target);
        while (value > observed)
        {
            long previous = Interlocked.CompareExchange(ref target, value, observed);
            if (previous == observed)
            {
                return;
            }
            observed = previous;
        }
    }

    private static long Exchange(ref long value) => Interlocked.Exchange(ref value, 0);

    private static long MillisecondsToTicks(double milliseconds) =>
        checked((long)Math.Round(milliseconds * Stopwatch.Frequency / 1_000.0));

    private static double TicksToMilliseconds(long ticks) =>
        ticks * 1_000.0 / Stopwatch.Frequency;

    private static double? AverageMilliseconds(long ticks, long count) =>
        count > 0 ? TicksToMilliseconds(ticks) / count : null;

    private static double? MaximumMilliseconds(long ticks, long count) =>
        count > 0 ? TicksToMilliseconds(ticks) : null;
}
