using P2PAudio.Windows.App.Services;

namespace P2PAudio.Windows.App.Tests;

public sealed class AudioFrameTimingClockTests
{
    [Fact]
    public void Next_UsesAudioFrameDurationForTimestampsAndDueTimes()
    {
        var clock = new AudioFrameTimingClock();
        clock.Reset(startTimestampMs: 10_000, startDueAtTickMs: 20_000);

        var first = clock.Next(sampleRate: 48_000, frameSamplesPerChannel: 960);
        var second = clock.Next(sampleRate: 48_000, frameSamplesPerChannel: 960);

        Assert.Equal(10_000, first.TimestampMs);
        Assert.Equal(20_000, first.DueAtTickMs);
        Assert.Equal(20, first.FrameDurationMs);
        Assert.Equal(10_020, second.TimestampMs);
        Assert.Equal(20_020, second.DueAtTickMs);
    }

    [Fact]
    public void Next_RoundsFrameDurationForCommon44100HzFrames()
    {
        var clock = new AudioFrameTimingClock();
        clock.Reset(startTimestampMs: 1_000, startDueAtTickMs: 2_000);

        var timing = clock.Next(sampleRate: 44_100, frameSamplesPerChannel: 882);

        Assert.Equal(20, timing.FrameDurationMs);
        Assert.Equal(1_000, timing.TimestampMs);
        Assert.Equal(2_000, timing.DueAtTickMs);
    }
}
