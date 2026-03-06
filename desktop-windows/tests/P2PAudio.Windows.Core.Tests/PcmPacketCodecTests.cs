using P2PAudio.Windows.Core.Audio;

namespace P2PAudio.Windows.Core.Tests;

public sealed class PcmPacketCodecTests
{
    [Fact]
    public void EncodeDecodePcm_RoundTrip()
    {
        var frame = new PcmFrame(
            Sequence: 7,
            TimestampMs: 123_456,
            SampleRate: 48_000,
            Channels: 2,
            BitsPerSample: 16,
            FrameSamplesPerChannel: 960,
            PcmBytes: Enumerable.Repeat((byte)0x2A, 3840).ToArray()
        );

        var packet = PcmPacketCodec.Encode(frame);
        var decoded = PcmPacketCodec.Decode(packet);

        Assert.NotNull(decoded);
        Assert.Equal(frame.Sequence, decoded!.Sequence);
        Assert.Equal(frame.TimestampMs, decoded.TimestampMs);
        Assert.Equal(frame.SampleRate, decoded.SampleRate);
        Assert.Equal(frame.Channels, decoded.Channels);
        Assert.Equal(frame.BitsPerSample, decoded.BitsPerSample);
        Assert.Equal(frame.FrameSamplesPerChannel, decoded.FrameSamplesPerChannel);
        Assert.Equal(frame.PcmBytes.Length, decoded.PcmBytes.Length);
    }
}
