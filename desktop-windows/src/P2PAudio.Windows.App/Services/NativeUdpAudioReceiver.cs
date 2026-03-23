using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using P2PAudio.Windows.App.Logging;
using P2PAudio.Windows.Core.Audio;
using P2PAudio.Windows.Core.Models;
using P2PAudio.Windows.Core.Networking;

namespace P2PAudio.Windows.App.Services;

public sealed class NativeUdpAudioReceiver : IUdpAudioReceiver, IDisposable
{
    private readonly ConcurrentQueue<PcmFrame> _frames = new();
    private readonly object _sync = new();
    private NativeOpusDecoder? _decoder;
    private UdpClient? _client;
    private Task? _receiveTask;
    private CancellationTokenSource? _receiveCts;
    private ConnectionDiagnostics _diagnostics = new(
        PathType: UsbTetheringDetector.ClassifyPrimaryPath(),
        SelectedCandidatePairType: "udp_opus"
    );
    private bool _disposed;
    private string _expectedRemoteHost = string.Empty;

    public TransportMode Mode => TransportMode.UdpOpus;

    public bool IsNativeBackend => true;

    public bool IsListening => _receiveCts is { IsCancellationRequested: false } && _client is not null;

    public Task<UdpAudioReceiverResult> StartListeningAsync(string expectedRemoteHost, int localPort)
    {
        EnsureNotDisposed();
        StopListening();

        try
        {
            var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.ReceiveBufferSize = UdpOpusPacketCodec.HeaderBytes * 512;
            client.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
            _client = client;
            _expectedRemoteHost = expectedRemoteHost ?? string.Empty;
            _receiveCts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
            _diagnostics = new ConnectionDiagnostics(
                PathType: UsbTetheringDetector.ClassifyPrimaryPath(),
                LocalCandidatesCount: 1,
                SelectedCandidatePairType: "udp_opus"
            );

            return Task.FromResult(
                new UdpAudioReceiverResult(
                    Success: true,
                    ErrorMessage: string.Empty,
                    StatusMessage: "Windows 側で UDP + Opus の受信待機を開始しました。",
                    Diagnostics: _diagnostics,
                    ReceiverPort: ((IPEndPoint)client.Client.LocalEndPoint!).Port
                )
            );
        }
        catch (Exception ex)
        {
            _diagnostics = _diagnostics with
            {
                FailureHint = "peer_unreachable",
                NormalizedFailureCode = FailureCode.PeerUnreachable
            };
            return Task.FromResult(
                new UdpAudioReceiverResult(
                    Success: false,
                    ErrorMessage: ex.Message,
                    StatusMessage: "UDP + Opus の受信待機を開始できませんでした。",
                    Diagnostics: _diagnostics,
                    ReceiverPort: localPort
                )
            );
        }
    }

    public bool TryReceivePcmFrame(out PcmFrame frame)
    {
        EnsureNotDisposed();
        return _frames.TryDequeue(out frame!);
    }

    public void StopListening()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _receiveCts = null;

        try
        {
            _client?.Close();
        }
        catch
        {
        }

        _client?.Dispose();
        _client = null;
        lock (_sync)
        {
            _decoder?.Dispose();
            _decoder = null;
        }

        while (_frames.TryDequeue(out _))
        {
        }
    }

    public ConnectionDiagnostics GetDiagnostics()
    {
        EnsureNotDisposed();
        return _diagnostics;
    }

    public BridgeBackendHealth GetBackendHealth()
    {
        return NativeOpusDecoder.HasBackend()
            ? new BridgeBackendHealth(
                IsReady: true,
                IsDevelopmentStub: false,
                Message: "UDP + Opus 受信モジュールを利用できます。",
                BlockingFailureCode: null)
            : new BridgeBackendHealth(
                IsReady: false,
                IsDevelopmentStub: false,
                Message: "UDP + Opus 受信モジュールを利用できません。",
                BlockingFailureCode: FailureCode.WebRtcNegotiationFailed);
    }

    public void Close()
    {
        if (_disposed)
        {
            return;
        }

        StopListening();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopListening();
        _disposed = true;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = _client;
                if (client is null)
                {
                    return;
                }

                var result = await client.ReceiveAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(_expectedRemoteHost) &&
                    !string.Equals(result.RemoteEndPoint.Address.ToString(), _expectedRemoteHost, StringComparison.Ordinal))
                {
                    continue;
                }

                var packet = UdpOpusPacketCodec.Decode(result.Buffer);
                if (packet is null)
                {
                    continue;
                }

                var frame = DecodePacket(packet);
                if (frame is null)
                {
                    continue;
                }

                while (_frames.Count >= 64 && _frames.TryDequeue(out _))
                {
                }

                _frames.Enqueue(frame);
                _diagnostics = _diagnostics with
                {
                    SelectedCandidatePairType = $"udp_opus <- {result.RemoteEndPoint.Address}"
                };
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                AppLogger.E("NativeUdpAudioReceiver", "udp_receive_failed", "UDP receive failed", exception: ex);
                _diagnostics = _diagnostics with
                {
                    FailureHint = "peer_unreachable",
                    NormalizedFailureCode = FailureCode.PeerUnreachable
                };
                return;
            }
        }
    }

    private PcmFrame? DecodePacket(UdpOpusPacket packet)
    {
        lock (_sync)
        {
            _decoder ??= new NativeOpusDecoder(packet.SampleRate, packet.Channels);
            if (!_decoder.MatchesFormat(packet.SampleRate, packet.Channels))
            {
                _decoder.Dispose();
                _decoder = new NativeOpusDecoder(packet.SampleRate, packet.Channels);
            }

            var pcmBytes = _decoder.Decode(packet.OpusPayload, packet.FrameSamplesPerChannel);
            if (pcmBytes.Length == 0)
            {
                return null;
            }

            return new PcmFrame(
                Sequence: packet.Sequence,
                TimestampMs: packet.TimestampMs,
                SampleRate: packet.SampleRate,
                Channels: packet.Channels,
                BitsPerSample: 16,
                FrameSamplesPerChannel: packet.FrameSamplesPerChannel,
                PcmBytes: pcmBytes
            );
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(NativeUdpAudioReceiver));
        }
    }

    private sealed class NativeOpusDecoder : IDisposable
    {
        private readonly nint _handle;
        private readonly int _sampleRate;
        private readonly int _channels;
        private bool _disposed;

        public NativeOpusDecoder(int sampleRate, int channels)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            var error = 0;
            _handle = opus_decoder_create(sampleRate, channels, ref error);
            if (_handle == 0 || error != 0)
            {
                throw new InvalidOperationException($"Opus decoder initialization failed: {error}");
            }
        }

        public static bool HasBackend()
        {
            try
            {
                return opus_get_version_string() != 0;
            }
            catch
            {
                return false;
            }
        }

        public bool MatchesFormat(int sampleRate, int channels)
        {
            return _sampleRate == sampleRate && _channels == channels;
        }

        public byte[] Decode(byte[] payload, int frameSamplesPerChannel)
        {
            if (_disposed || payload.Length == 0)
            {
                return [];
            }

            var samples = new short[frameSamplesPerChannel * _channels];
            var decodedSamplesPerChannel = opus_decode(
                _handle,
                payload,
                payload.Length,
                samples,
                frameSamplesPerChannel,
                0
            );
            if (decodedSamplesPerChannel <= 0)
            {
                return [];
            }

            var pcmBytes = new byte[decodedSamplesPerChannel * _channels * sizeof(short)];
            Buffer.BlockCopy(samples, 0, pcmBytes, 0, pcmBytes.Length);
            return pcmBytes;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_handle != 0)
            {
                opus_decoder_destroy(_handle);
            }
        }

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint opus_decoder_create(int fs, int channels, ref int error);

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void opus_decoder_destroy(nint st);

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int opus_decode(
            nint st,
            byte[] data,
            int len,
            short[] pcm,
            int frame_size,
            int decode_fec);

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint opus_get_version_string();
    }
}
