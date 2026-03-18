using NAudio.Wave;
using P2PAudio.Windows.App.Logging;
using P2PAudio.Windows.Core.Audio;

namespace P2PAudio.Windows.App.Services;

public sealed class LoopbackPcmSender : IDisposable
{
    private const long StatsLogIntervalMs = 5_000;

    private readonly Func<byte[], bool> _sendPacket;
    private readonly object _sync = new();
    private WasapiLoopbackCapture? _capture;
    private readonly List<byte> _pendingPcm16 = [];
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
    {
        _sendPacket = sendPacket;
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync) return _isRunning;
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
            _sampleRate = _capture.WaveFormat.SampleRate;
            _channels = _capture.WaveFormat.Channels;
            _frameSamples = Math.Max(1, _sampleRate / 50); // 20ms frames.
            _sequence = 0;
            _sentFrames = 0;
            _sendFailures = 0;
            _lastStatsLogAtMs = Environment.TickCount64;
            _lastSendFailureLogAtMs = 0;
            _pendingPcm16.Clear();

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
                    ["sampleRate"] = _sampleRate,
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

        if (capture != null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            capture.StopRecording();
            capture.Dispose();
        }

        lock (_sync)
        {
            _pendingPcm16.Clear();
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

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        List<PendingPacket> packetsToSend;
        lock (_sync)
        {
            if (!_isRunning || _capture == null)
            {
                return;
            }

            AppendAsPcm16(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
            packetsToSend = DrainPacketsUnsafe();
        }

        foreach (var packet in packetsToSend)
        {
            if (_sendPacket(packet.Packet))
            {
                _sentFrames++;
            }
            else
            {
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
                            ["sequence"] = packet.Sequence,
                            ["sampleRate"] = packet.SampleRate,
                            ["channels"] = packet.Channels,
                            ["frameSamplesPerChannel"] = packet.FrameSamplesPerChannel,
                            ["sendFailures"] = _sendFailures
                        }
                    );
                }
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
        _pendingPcm16.AddRange(normalized.PcmBytes);
    }

    private List<PendingPacket> DrainPacketsUnsafe()
    {
        var packets = new List<PendingPacket>();
        var frameBytes = _frameSamples * _channels * 2;
        while (_pendingPcm16.Count >= frameBytes)
        {
            var pcm = _pendingPcm16.GetRange(0, frameBytes).ToArray();
            _pendingPcm16.RemoveRange(0, frameBytes);

            var frame = new PcmFrame(
                Sequence: _sequence++,
                TimestampMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SampleRate: _sampleRate,
                Channels: _channels,
                BitsPerSample: 16,
                FrameSamplesPerChannel: _frameSamples,
                PcmBytes: pcm
            );
            packets.Add(new PendingPacket(
                Sequence: frame.Sequence,
                SampleRate: frame.SampleRate,
                Channels: frame.Channels,
                FrameSamplesPerChannel: frame.FrameSamplesPerChannel,
                Packet: PcmPacketCodec.Encode(frame)
            ));
        }
        return packets;
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

    public void Dispose()
    {
        Stop();
    }

    private sealed record PendingPacket(
        int Sequence,
        int SampleRate,
        int Channels,
        int FrameSamplesPerChannel,
        byte[] Packet
    );
}
