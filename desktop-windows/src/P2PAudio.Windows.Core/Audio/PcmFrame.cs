namespace P2PAudio.Windows.Core.Audio;

public sealed record PcmFrame(
    int Sequence,
    long TimestampMs,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    int FrameSamplesPerChannel,
    byte[] PcmBytes
);
