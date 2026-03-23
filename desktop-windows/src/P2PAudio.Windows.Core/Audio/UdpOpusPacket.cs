namespace P2PAudio.Windows.Core.Audio;

public sealed record UdpOpusPacket(
    int Sequence,
    long TimestampMs,
    int SampleRate,
    int Channels,
    int FrameSamplesPerChannel,
    byte[] OpusPayload
);
