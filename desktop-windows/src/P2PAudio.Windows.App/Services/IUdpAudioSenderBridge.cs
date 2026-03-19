using P2PAudio.Windows.Core.Audio;
using P2PAudio.Windows.Core.Models;

namespace P2PAudio.Windows.App.Services;

public interface IUdpAudioSenderBridge : IAudioTransportBackend
{
    Task<UdpAudioSenderResult> StartStreamingAsync(string remoteHost, int remotePort, string remoteServiceName);
    bool SendPcmFrame(PcmFrame frame);
    void StopStreaming();
    bool IsStreaming { get; }
}

public sealed record UdpAudioSenderResult(
    bool Success,
    string ErrorMessage,
    string StatusMessage,
    ConnectionDiagnostics Diagnostics
);
