namespace P2PAudio.Windows.Core.Audio;

public sealed class PcmSequenceGapConcealer
{
    private const int MaxConcealedGapFrames = 6;

    private string? _formatKey;
    private int? _expectedSequence;

    public PcmGapConcealmentResult Prepare(PcmFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var formatKey = $"{frame.SampleRate}:{frame.Channels}:{frame.BitsPerSample}:{frame.FrameSamplesPerChannel}:{frame.PcmBytes.Length}";
        var formatChanged = _formatKey is not null && !string.Equals(_formatKey, formatKey, StringComparison.Ordinal);
        if (formatChanged)
        {
            Reset();
        }

        _formatKey = formatKey;

        if (_expectedSequence is null)
        {
            _expectedSequence = frame.Sequence + 1;
            return new PcmGapConcealmentResult(
                PlaybackFrames: [frame.PcmBytes],
                InsertedSilenceFrames: 0,
                SkippedDiscontinuityFrames: 0,
                DroppedLateFrame: false,
                FormatChanged: formatChanged
            );
        }

        if (frame.Sequence < _expectedSequence.Value)
        {
            return new PcmGapConcealmentResult(
                PlaybackFrames: [],
                InsertedSilenceFrames: 0,
                SkippedDiscontinuityFrames: 0,
                DroppedLateFrame: true,
                FormatChanged: formatChanged
            );
        }

        var missingFrames = frame.Sequence - _expectedSequence.Value;
        _expectedSequence = frame.Sequence + 1;

        if (missingFrames > MaxConcealedGapFrames)
        {
            return new PcmGapConcealmentResult(
                PlaybackFrames: [frame.PcmBytes],
                InsertedSilenceFrames: 0,
                SkippedDiscontinuityFrames: missingFrames,
                DroppedLateFrame: false,
                FormatChanged: formatChanged
            );
        }

        if (missingFrames == 0)
        {
            return new PcmGapConcealmentResult(
                PlaybackFrames: [frame.PcmBytes],
                InsertedSilenceFrames: 0,
                SkippedDiscontinuityFrames: 0,
                DroppedLateFrame: false,
                FormatChanged: formatChanged
            );
        }

        var playbackFrames = new byte[missingFrames + 1][];
        for (var index = 0; index < missingFrames; index++)
        {
            playbackFrames[index] = new byte[frame.PcmBytes.Length];
        }
        playbackFrames[^1] = frame.PcmBytes;

        return new PcmGapConcealmentResult(
            PlaybackFrames: playbackFrames,
            InsertedSilenceFrames: missingFrames,
            SkippedDiscontinuityFrames: 0,
            DroppedLateFrame: false,
            FormatChanged: formatChanged
        );
    }

    public void Reset()
    {
        _formatKey = null;
        _expectedSequence = null;
    }
}

public sealed record PcmGapConcealmentResult(
    IReadOnlyList<byte[]> PlaybackFrames,
    int InsertedSilenceFrames,
    int SkippedDiscontinuityFrames,
    bool DroppedLateFrame,
    bool FormatChanged
);
