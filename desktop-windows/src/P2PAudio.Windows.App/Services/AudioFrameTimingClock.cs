namespace P2PAudio.Windows.App.Services;

public sealed class AudioFrameTimingClock
{
    private long _nextTimestampMs;
    private long _nextDueAtTickMs;

    public void Reset(long startTimestampMs, long startDueAtTickMs)
    {
        _nextTimestampMs = startTimestampMs;
        _nextDueAtTickMs = startDueAtTickMs;
    }

    public ScheduledFrameTiming Next(int sampleRate, int frameSamplesPerChannel)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (frameSamplesPerChannel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameSamplesPerChannel));
        }

        var frameDurationMs = Math.Max(1, (int)Math.Round((frameSamplesPerChannel * 1000d) / sampleRate));
        var timing = new ScheduledFrameTiming(
            TimestampMs: _nextTimestampMs,
            DueAtTickMs: _nextDueAtTickMs,
            FrameDurationMs: frameDurationMs
        );
        _nextTimestampMs += frameDurationMs;
        _nextDueAtTickMs += frameDurationMs;
        return timing;
    }
}

public readonly record struct ScheduledFrameTiming(
    long TimestampMs,
    long DueAtTickMs,
    int FrameDurationMs
);
