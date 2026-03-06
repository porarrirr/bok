using NAudio.Wave;
using P2PAudio.Windows.Core.Audio;

namespace P2PAudio.Windows.App.Services;

public sealed class PcmPlaybackService : IDisposable
{
    private readonly object _sync = new();

    private WaveOutEvent? _output;
    private BufferedWaveProvider? _bufferedProvider;
    private WaveFormat? _currentFormat;

    public void PlayPacket(byte[] packet)
    {
        var frame = PcmPacketCodec.Decode(packet);
        if (frame is null)
        {
            return;
        }

        lock (_sync)
        {
            EnsureOutput(frame.SampleRate, frame.Channels);
            _bufferedProvider!.AddSamples(frame.PcmBytes, 0, frame.PcmBytes.Length);
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

        _output?.Stop();
        _output?.Dispose();

        _currentFormat = new WaveFormat(sampleRate, 16, channels);
        _bufferedProvider = new BufferedWaveProvider(_currentFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(3),
            DiscardOnBufferOverflow = true
        };
        _output = new WaveOutEvent();
        _output.Init(_bufferedProvider);
        _output.Play();
    }

    public void Dispose()
    {
        Stop();
    }
}
