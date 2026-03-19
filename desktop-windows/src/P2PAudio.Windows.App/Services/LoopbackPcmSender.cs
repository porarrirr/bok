using NAudio.Wave;
using P2PAudio.Windows.App.Logging;
using P2PAudio.Windows.Core.Audio;

namespace P2PAudio.Windows.App.Services;

public sealed class LoopbackPcmSender : ILoopbackAudioSender
{
    private const long StatsLogIntervalMs = 5_000;
    private const int DefaultFramesPerSecond = 50;
    private const int ResampleBufferBytes = 16_384;

    private readonly Func<PcmFrame, bool> _sendFrame;
    private readonly object _sync = new();
    private readonly LoopbackCaptureOptions _options;
    private readonly List<byte> _pendingPcm16 = [];
    private WasapiLoopbackCapture? _capture;
    private BufferedWaveProvider? _resampleInputBuffer;
    private MediaFoundationResampler? _resampler;
    private int _sampleRate;
    private int _channels;
    private int _frameSamples;
    private int _sequence;
    private bool _isRunning;
    private long _sentFrames;
    private long _sendFailures;
    private long _lastStatsLogAtMs = Environment.TickCount64;
    private long _lastSendFailureLogAtMs;

    public LoopbackPcmSender(Func<byte[], bool> sendPacket)
        : this(frame => sendPacket(PcmPacketCodec.Encode(frame)))
    {
    }

    public LoopbackPcmSender(Func<PcmFrame, bool> sendFrame, LoopbackCaptureOptions? options = null)
    {
        _sendFrame = sendFrame ?? throw new ArgumentNullException(nameof(sendFrame));
        _options = options ?? new LoopbackCaptureOptions();
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _isRunning;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_isRunning)
            {
                return;
            }

            _capture = new WasapiLoopbackCapture();
            _sampleRate = ResolveOutputSampleRate(_capture.WaveFormat.SampleRate);
            _channels = PcmCaptureNormalizer.GetOutputChannels(_capture.WaveFormat.Channels);
            _frameSamples = Math.Max(1, _sampleRate / _options.FramesPerSecond);
            _sequence = 0;
            _sentFrames = 0;
            _sendFailures = 0;
            _lastStatsLogAtMs = Environment.TickCount64;
            _lastSendFailureLogAtMs = 0;
            _pendingPcm16.Clear();
            ConfigureResamplerUnsafe(_capture.WaveFormat.SampleRate, _channels);

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _isRunning = true;

            AppLogger.I(
                "LoopbackSender",
                "capture_started",
                "WASAPI loopback capture started",
                new Dictionary<string, object?>
                {
                    ["inputSampleRate"] = _capture.WaveFormat.SampleRate,
                    ["outputSampleRate"] = _sampleRate,
                    ["channels"] = _channels,
                    ["bitsPerSample"] = _capture.WaveFormat.BitsPerSample,
                    ["encoding"] = _capture.WaveFormat.Encoding.ToString()
                }
            );
        }
    }

    public void Stop()
    {
        WasapiLoopbackCapture? capture;
        lock (_sync)
        {
            if (!_isRunning)
            {
                return;
            }

            capture = _capture;
            _capture = null;
            _isRunning = false;
        }

        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            capture.StopRecording();
            capture.Dispose();
        }

        lock (_sync)
        {
            _pendingPcm16.Clear();
            DisposeResamplerUnsafe();
        }

        AppLogger.I(
            "LoopbackSender",
            "capture_stopped",
            "WASAPI loopback capture stopped",
            new Dictionary<string, object?>
            {
                ["sentFrames"] = _sentFrames,
                ["sendFailures"] = _sendFailures
            }
        );
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        List<PendingFrame> framesToSend;
        lock (_sync)
        {
            if (!_isRunning || _capture is null)
            {
                return;
            }

            AppendAsPcm16(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
            framesToSend = DrainFramesUnsafe();
        }

        foreach (var pendingFrame in framesToSend)
        {
            if (_sendFrame(pendingFrame.Frame))
            {
                _sentFrames++;
                continue;
            }

            _sendFailures++;
            var now = Environment.TickCount64;
            if (now - _lastSendFailureLogAtMs >= 1_000)
            {
                _lastSendFailureLogAtMs = now;
                AppLogger.W(
                    "LoopbackSender",
                    "pcm_send_failed",
                    "Failed to send loopback PCM frame",
                    new Dictionary<string, object?>
                    {
                        ["sequence"] = pendingFrame.Frame.Sequence,
                        ["sampleRate"] = pendingFrame.Frame.SampleRate,
                        ["channels"] = pendingFrame.Frame.Channels,
                        ["frameSamplesPerChannel"] = pendingFrame.Frame.FrameSamplesPerChannel,
                        ["sendFailures"] = _sendFailures
                    }
                );
            }
        }

        LogStatsIfNeeded();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_sync)
        {
            _isRunning = false;
            _capture = null;
            _pendingPcm16.Clear();
            DisposeResamplerUnsafe();
        }

        if (e.Exception is not null)
        {
            AppLogger.E(
                "LoopbackSender",
                "capture_stopped_with_error",
                "Loopback capture stopped with an error",
                exception: e.Exception
            );
            return;
        }

        AppLogger.I("LoopbackSender", "capture_stopped_event", "Loopback capture stopped");
    }

    private void AppendAsPcm16(byte[] input, int bytesRecorded, WaveFormat format)
    {
        PcmNormalizationResult? normalized = null;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            normalized = PcmCaptureNormalizer.NormalizeFloat32(input.AsSpan(0, bytesRecorded), format.Channels);
        }
        else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            normalized = PcmCaptureNormalizer.NormalizePcm16(input.AsSpan(0, bytesRecorded), format.Channels);
        }

        if (normalized is null || normalized.PcmBytes.Length == 0)
        {
            return;
        }

        _channels = normalized.Channels;
        _sampleRate = ResolveOutputSampleRate(format.SampleRate);
        _frameSamples = Math.Max(1, _sampleRate / _options.FramesPerSecond);

        if (_resampler is null)
        {
            _pendingPcm16.AddRange(normalized.PcmBytes);
            return;
        }

        _resampleInputBuffer!.AddSamples(normalized.PcmBytes, 0, normalized.PcmBytes.Length);
        DrainResampledPcmUnsafe();
    }

    private void DrainResampledPcmUnsafe()
    {
        var resampledBuffer = new byte[ResampleBufferBytes];
        while (true)
        {
            var bytesRead = _resampler!.Read(resampledBuffer, 0, resampledBuffer.Length);
            if (bytesRead <= 0)
            {
                return;
            }

            var chunk = new byte[bytesRead];
            Buffer.BlockCopy(resampledBuffer, 0, chunk, 0, bytesRead);
            _pendingPcm16.AddRange(chunk);
            if (bytesRead < resampledBuffer.Length)
            {
                return;
            }
        }
    }

    private void ConfigureResamplerUnsafe(int inputSampleRate, int channels)
    {
        DisposeResamplerUnsafe();

        var targetSampleRate = _options.TargetSampleRate;
        if (targetSampleRate is null || targetSampleRate == inputSampleRate)
        {
            return;
        }

        var inputFormat = new WaveFormat(inputSampleRate, 16, channels);
        _resampleInputBuffer = new BufferedWaveProvider(inputFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
            ReadFully = false
        };
        _resampler = new MediaFoundationResampler(
            _resampleInputBuffer,
            new WaveFormat(targetSampleRate.Value, 16, channels))
        {
            ResamplerQuality = 60
        };
    }

    private void DisposeResamplerUnsafe()
    {
        _resampler?.Dispose();
        _resampler = null;
        _resampleInputBuffer = null;
    }

    private int ResolveOutputSampleRate(int inputSampleRate)
    {
        return _options.TargetSampleRate ?? inputSampleRate;
    }

    private List<PendingFrame> DrainFramesUnsafe()
    {
        var frames = new List<PendingFrame>();
        var frameBytes = _frameSamples * _channels * sizeof(short);
        while (_pendingPcm16.Count >= frameBytes)
        {
            var pcm = _pendingPcm16.GetRange(0, frameBytes).ToArray();
            _pendingPcm16.RemoveRange(0, frameBytes);

            frames.Add(new PendingFrame(
                new PcmFrame(
                    Sequence: _sequence++,
                    TimestampMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    SampleRate: _sampleRate,
                    Channels: _channels,
                    BitsPerSample: 16,
                    FrameSamplesPerChannel: _frameSamples,
                    PcmBytes: pcm
                )
            ));
        }

        return frames;
    }

    private void LogStatsIfNeeded()
    {
        var now = Environment.TickCount64;
        if (now - _lastStatsLogAtMs < StatsLogIntervalMs)
        {
            return;
        }

        _lastStatsLogAtMs = now;
        int pendingPcmBytes;
        lock (_sync)
        {
            pendingPcmBytes = _pendingPcm16.Count;
        }

        AppLogger.D(
            "LoopbackSender",
            "sender_stats",
            "Loopback sender stats",
            new Dictionary<string, object?>
            {
                ["sentFrames"] = _sentFrames,
                ["sendFailures"] = _sendFailures,
                ["pendingPcmBytes"] = pendingPcmBytes,
                ["sampleRate"] = _sampleRate,
                ["channels"] = _channels
            }
        );
    }

    private sealed record PendingFrame(PcmFrame Frame);
}

public sealed class LoopbackCaptureOptions
{
    private const int DefaultFramesPerSecond = 50;

    public LoopbackCaptureOptions(int? targetSampleRate = null, int framesPerSecond = DefaultFramesPerSecond)
    {
        if (targetSampleRate is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
        }

        if (framesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        }

        TargetSampleRate = targetSampleRate;
        FramesPerSecond = framesPerSecond;
    }

    public int? TargetSampleRate { get; }

    public int FramesPerSecond { get; }
}
