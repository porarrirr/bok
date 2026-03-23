using P2PAudio.Windows.Core.Audio;
using P2PAudio.Windows.Core.Models;

namespace P2PAudio.Windows.App.Services;

public interface IUdpAudioReceiver : IAudioTransportBackend
{
    Task<UdpAudioReceiverResult> StartListeningAsync(string expectedRemoteHost, int localPort);
    bool TryReceivePcmFrame(out PcmFrame frame);
    void StopListening();
    bool IsListening { get; }
}

public sealed record UdpAudioReceiverResult(
    bool Success,
    string ErrorMessage,
    string StatusMessage,
    ConnectionDiagnostics Diagnostics,
    int ReceiverPort
);
