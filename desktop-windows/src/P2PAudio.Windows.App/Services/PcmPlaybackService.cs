using NAudio.Wave;
using P2PAudio.Windows.App.Logging;
using P2PAudio.Windows.Core.Audio;

namespace P2PAudio.Windows.App.Services;

public sealed class PcmPlaybackService : IDisposable
{
    public static readonly PcmPlaybackProfile DefaultProfile = new(
        DesiredLatencyMs: 80,
        BufferDurationMs: 500
    );

    private const int MaxLoggedBufferLeadMs = 350;
    private const long StatsLogIntervalMs = 5_000;

    private readonly object _sync = new();
    private readonly PcmSequenceGapConcealer _gapConcealer = new();
    private PcmPlaybackProfile _profile;

    private WaveOutEvent? _output;
    private BufferedWaveProvider? _bufferedProvider;
    private WaveFormat? _currentFormat;
    private string? _currentFormatKey;
    private long _playedFrames;
    private long _insertedSilenceFrames;
    private long _lateFramesDropped;
    private long _sequenceDiscontinuities;
    private long _lastStatsLogAtMs = Environment.TickCount64;
    private long _lastWarningLogAtMs;

    public PcmPlaybackService() : this(DefaultProfile)
    {
    }

    public PcmPlaybackService(PcmPlaybackProfile profile)
    {
        _profile = profile;
    }

    public bool PlayPacket(byte[] packet)
    {
        var frame = PcmPacketCodec.Decode(packet);
        if (frame is null)
        {
            AppLogger.W("PcmPlayback", "pcm_decode_failed", "Dropped invalid PCM packet");
            return false;
        }

        lock (_sync)
        {
            return PlayFrameUnsafe(frame);
        }
    }

    public bool PlayFrame(PcmFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_sync)
        {
            return PlayFrameUnsafe(frame);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _output?.Stop();
            _output?.Dispose();
            _output = null;
            _bufferedProvider = null;
            _currentFormat = null;
            _currentFormatKey = null;
            _gapConcealer.Reset();
        }

        AppLogger.I(
            "PcmPlayback",
            "playback_stop",
            "PCM playback stopped",
            new Dictionary<string, object?>
            {
                ["playedFrames"] = _playedFrames,
                ["insertedSilenceFrames"] = _insertedSilenceFrames,
                ["lateFramesDropped"] = _lateFramesDropped,
                ["skippedDiscontinuityFrames"] = _sequenceDiscontinuities
            }
        );
        _playedFrames = 0;
        _insertedSilenceFrames = 0;
        _lateFramesDropped = 0;
        _sequenceDiscontinuities = 0;
        _lastStatsLogAtMs = Environment.TickCount64;
        _lastWarningLogAtMs = 0;
    }

    public void UpdateProfile(PcmPlaybackProfile profile)
    {
        lock (_sync)
        {
            _profile = profile;
            if (_currentFormat is not null)
            {
                RecreateOutputUnsafe(_currentFormat.SampleRate, _currentFormat.Channels);
            }
        }
    }

    private void EnsureOutput(int sampleRate, int channels)
    {
        if (sampleRate <= 0 || channels <= 0)
        {
            return;
        }

        if (_currentFormat is not null &&
            _currentFormat.SampleRate == sampleRate &&
            _currentFormat.Channels == channels &&
            _bufferedProvider is not null &&
            _output is not null)
        {
            if (_output.PlaybackState != PlaybackState.Playing)
            {
                _output.Play();
            }
            return;
        }

        RecreateOutputUnsafe(sampleRate, channels);
    }

    private void RecreateOutputUnsafe(int sampleRate, int channels)
    {
        _output?.Stop();
        _output?.Dispose();

        _currentFormat = new WaveFormat(sampleRate, 16, channels);
        _currentFormatKey = $"{sampleRate}-{channels}";
        _bufferedProvider = new BufferedWaveProvider(_currentFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(_profile.BufferDurationMs),
            DiscardOnBufferOverflow = true
        };
        _output = new WaveOutEvent
        {
            DesiredLatency = _profile.DesiredLatencyMs,
            NumberOfBuffers = 3
        };
        _output.Init(_bufferedProvider);
        _output.Play();
        AppLogger.I(
            "PcmPlayback",
            "playback_output_ready",
            "PCM playback output initialized",
            new Dictionary<string, object?>
            {
                ["sampleRate"] = sampleRate,
                ["channels"] = channels,
                ["desiredLatencyMs"] = _profile.DesiredLatencyMs,
                ["bufferDurationMs"] = _profile.BufferDurationMs
            }
        );
    }

    private bool PlayFrameUnsafe(PcmFrame frame)
    {
        EnsureOutput(frame.SampleRate, frame.Channels);
        if (_bufferedProvider is null)
        {
            return false;
        }

        var concealment = _gapConcealer.Prepare(frame);
        if (concealment.FormatChanged)
        {
            AppLogger.I(
                "PcmPlayback",
                "pcm_format_changed",
                "Playback format changed; reset concealment state",
                new Dictionary<string, object?>
                {
                    ["sampleRate"] = frame.SampleRate,
                    ["channels"] = frame.Channels,
                    ["frameSamplesPerChannel"] = frame.FrameSamplesPerChannel
                }
            );
        }

        if (concealment.DroppedLateFrame)
        {
            _lateFramesDropped++;
            LogWarningIfNeeded(
                "pcm_late_frame_dropped",
                "Dropped late PCM frame",
                new Dictionary<string, object?>
                {
                    ["sequence"] = frame.Sequence,
                    ["lateFramesDropped"] = _lateFramesDropped
                }
            );
            return true;
        }

        if (concealment.InsertedSilenceFrames > 0)
        {
            _insertedSilenceFrames += concealment.InsertedSilenceFrames;
            LogWarningIfNeeded(
                "pcm_gap_concealed",
                "Inserted silence to conceal missing PCM frames",
                new Dictionary<string, object?>
                {
                    ["sequence"] = frame.Sequence,
                    ["insertedSilenceFrames"] = concealment.InsertedSilenceFrames,
                    ["totalInsertedSilenceFrames"] = _insertedSilenceFrames
                }
            );
        }

        if (concealment.SkippedDiscontinuityFrames > 0)
        {
            _sequenceDiscontinuities += concealment.SkippedDiscontinuityFrames;
            LogWarningIfNeeded(
                "pcm_large_gap_skipped",
                "Skipped a large PCM discontinuity without stretching silence",
                new Dictionary<string, object?>
                {
                    ["sequence"] = frame.Sequence,
                    ["skippedFrames"] = concealment.SkippedDiscontinuityFrames,
                    ["totalSkippedFrames"] = _sequenceDiscontinuities
                }
            );
        }

        foreach (var playbackFrame in concealment.PlaybackFrames)
        {
            if (_bufferedProvider.BufferedBytes + playbackFrame.Length > _bufferedProvider.BufferLength)
            {
                LogWarningIfNeeded(
                    "pcm_buffer_overflow_imminent",
                    "Playback buffer is close to overflow",
                    new Dictionary<string, object?>
                    {
                        ["bufferedBytes"] = _bufferedProvider.BufferedBytes,
                        ["bufferLength"] = _bufferedProvider.BufferLength,
                        ["frameBytes"] = playbackFrame.Length
                    }
                );
            }

            _bufferedProvider.AddSamples(playbackFrame, 0, playbackFrame.Length);
            _playedFrames++;
        }

        LogStatsIfNeededUnsafe();
        return true;
    }

    private void LogStatsIfNeededUnsafe()
    {
        var now = Environment.TickCount64;
        if (now - _lastStatsLogAtMs < StatsLogIntervalMs || _bufferedProvider is null || _currentFormat is null)
        {
            return;
        }

        _lastStatsLogAtMs = now;
        var bufferedMs = _currentFormat.AverageBytesPerSecond <= 0
            ? 0
            : (_bufferedProvider.BufferedBytes * 1000L) / _currentFormat.AverageBytesPerSecond;

        var logLevel = bufferedMs >= MaxLoggedBufferLeadMs ? "buffer_pressure" : "playback_stats";
        if (bufferedMs >= MaxLoggedBufferLeadMs)
        {
            AppLogger.W(
                "PcmPlayback",
                logLevel,
                "Playback buffer is running hot",
                new Dictionary<string, object?>
                {
                    ["format"] = _currentFormatKey ?? "unknown",
                    ["bufferedMs"] = bufferedMs,
                    ["playedFrames"] = _playedFrames,
                    ["insertedSilenceFrames"] = _insertedSilenceFrames,
                    ["lateFramesDropped"] = _lateFramesDropped,
                    ["skippedDiscontinuityFrames"] = _sequenceDiscontinuities
                }
            );
            return;
        }

        AppLogger.D(
            "PcmPlayback",
            logLevel,
            "Playback stats",
            new Dictionary<string, object?>
            {
                ["format"] = _currentFormatKey ?? "unknown",
                ["bufferedMs"] = bufferedMs,
                ["playedFrames"] = _playedFrames,
                ["insertedSilenceFrames"] = _insertedSilenceFrames,
                ["lateFramesDropped"] = _lateFramesDropped,
                ["skippedDiscontinuityFrames"] = _sequenceDiscontinuities
            }
        );
    }

    private void LogWarningIfNeeded(string eventName, string message, IReadOnlyDictionary<string, object?> context)
    {
        var now = Environment.TickCount64;
        if (now - _lastWarningLogAtMs < 1_000)
        {
            return;
        }

        _lastWarningLogAtMs = now;
        AppLogger.W("PcmPlayback", eventName, message, context);
    }

    public void Dispose()
    {
        Stop();
    }
}

public sealed record PcmPlaybackProfile(
    int DesiredLatencyMs,
    int BufferDurationMs
);
