namespace P2PAudio.Windows.App.Services;

public sealed record LoopbackAudioDiagnostics(
    bool IsActive = false,
    int SampleRate = 0,
    int Channels = 0,
    int BitsPerSample = 0,
    int FrameSamplesPerChannel = 0,
    int FrameDurationMs = 0,
    int FramesPerSecond = 0,
    int PendingPcmBytes = 0,
    int PendingSendFrames = 0,
    long SentFrames = 0,
    long SendFailures = 0,
    long PendingSendDrops = 0,
    bool PacingEnabled = false
);
