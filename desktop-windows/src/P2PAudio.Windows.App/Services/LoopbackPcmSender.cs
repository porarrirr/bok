using NAudio.Wave;
using P2PAudio.Windows.Core.Audio;

namespace P2PAudio.Windows.App.Services;

public sealed class LoopbackPcmSender : IDisposable
{
    private readonly Func<byte[], bool> _sendPacket;
    private readonly object _sync = new();
    private WasapiLoopbackCapture? _capture;
    private readonly List<byte> _pendingPcm16 = [];
    private int _sampleRate;
    private int _channels;
    private int _frameSamples;
    private int _sequence;
    private bool _isRunning;

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
            _pendingPcm16.Clear();

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _isRunning = true;
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
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_sync)
        {
            if (!_isRunning || _capture == null)
            {
                return;
            }

            AppendAsPcm16(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
            FlushFrames();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_sync)
        {
            _isRunning = false;
            _capture = null;
            _pendingPcm16.Clear();
        }
    }

    private void AppendAsPcm16(byte[] input, int bytesRecorded, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (var i = 0; i + 3 < bytesRecorded; i += 4)
            {
                var sample = BitConverter.ToSingle(input, i);
                sample = Math.Clamp(sample, -1.0f, 1.0f);
                var int16 = (short)(sample * short.MaxValue);
                _pendingPcm16.Add((byte)(int16 & 0xFF));
                _pendingPcm16.Add((byte)((int16 >> 8) & 0xFF));
            }
            return;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            for (var i = 0; i < bytesRecorded; i++)
            {
                _pendingPcm16.Add(input[i]);
            }
        }
    }

    private void FlushFrames()
    {
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
            var packet = PcmPacketCodec.Encode(frame);
            _ = _sendPacket(packet);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
