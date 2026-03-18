using P2PAudio.Windows.Core.Audio;

namespace P2PAudio.Windows.Core.Tests;

public sealed class PcmSequenceGapConcealerTests
{
    private static PcmFrame CreateFrame(
        int sequence,
        int sampleRate = 48_000,
        int channels = 2,
        int frameSamplesPerChannel = 960,
        int byteSeed = 0x2A) =>
        new(
            Sequence: sequence,
            TimestampMs: 123_456 + sequence,
            SampleRate: sampleRate,
            Channels: channels,
            BitsPerSample: 16,
            FrameSamplesPerChannel: frameSamplesPerChannel,
            PcmBytes: Enumerable.Repeat((byte)byteSeed, frameSamplesPerChannel * channels * 2).ToArray()
        );

    [Fact]
    public void Prepare_FirstFrame_PlaysImmediately()
    {
        var concealer = new PcmSequenceGapConcealer();

        var result = concealer.Prepare(CreateFrame(sequence: 5));

        Assert.Single(result.PlaybackFrames);
        Assert.Equal(0, result.InsertedSilenceFrames);
        Assert.Equal(0, result.SkippedDiscontinuityFrames);
        Assert.False(result.DroppedLateFrame);
        Assert.False(result.FormatChanged);
    }

    [Fact]
    public void Prepare_MissingSingleFrame_InsertsSilenceBeforePlayback()
    {
        var concealer = new PcmSequenceGapConcealer();
        _ = concealer.Prepare(CreateFrame(sequence: 0, byteSeed: 0x11));

        var result = concealer.Prepare(CreateFrame(sequence: 2, byteSeed: 0x33));

        Assert.Equal(2, result.PlaybackFrames.Count);
        Assert.Equal(1, result.InsertedSilenceFrames);
        Assert.All(result.PlaybackFrames[0], sample => Assert.Equal((byte)0, sample));
        Assert.Equal((byte)0x33, result.PlaybackFrames[1][0]);
    }

    [Fact]
    public void Prepare_LateFrame_IsDropped()
    {
        var concealer = new PcmSequenceGapConcealer();
        _ = concealer.Prepare(CreateFrame(sequence: 10));
        _ = concealer.Prepare(CreateFrame(sequence: 11));

        var result = concealer.Prepare(CreateFrame(sequence: 10));

        Assert.Empty(result.PlaybackFrames);
        Assert.True(result.DroppedLateFrame);
    }

    [Fact]
    public void Prepare_LargeGap_SkipsDiscontinuityWithoutLongSilence()
    {
        var concealer = new PcmSequenceGapConcealer();
        _ = concealer.Prepare(CreateFrame(sequence: 1));

        var result = concealer.Prepare(CreateFrame(sequence: 12));

        Assert.Single(result.PlaybackFrames);
        Assert.Equal(0, result.InsertedSilenceFrames);
        Assert.Equal(10, result.SkippedDiscontinuityFrames);
    }

    [Fact]
    public void Prepare_FormatChange_ResetsExpectedSequence()
    {
        var concealer = new PcmSequenceGapConcealer();
        _ = concealer.Prepare(CreateFrame(sequence: 1, sampleRate: 48_000));

        var result = concealer.Prepare(CreateFrame(sequence: 20, sampleRate: 44_100, frameSamplesPerChannel: 882));

        Assert.True(result.FormatChanged);
        Assert.Single(result.PlaybackFrames);
        Assert.Equal(0, result.InsertedSilenceFrames);
    }
}
