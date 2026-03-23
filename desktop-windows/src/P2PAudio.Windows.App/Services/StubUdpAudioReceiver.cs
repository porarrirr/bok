using P2PAudio.Windows.Core.Audio;
using P2PAudio.Windows.Core.Models;

namespace P2PAudio.Windows.App.Services;

public sealed class StubUdpAudioReceiver : IUdpAudioReceiver
{
    private readonly string _startupReason;

    public StubUdpAudioReceiver(string startupReason)
    {
        _startupReason = startupReason;
    }

    public TransportMode Mode => TransportMode.UdpOpus;

    public bool IsNativeBackend => false;

    public bool IsListening => false;

    public Task<UdpAudioReceiverResult> StartListeningAsync(string expectedRemoteHost, int localPort)
    {
        _ = expectedRemoteHost;
        _ = localPort;
        return Task.FromResult(
            new UdpAudioReceiverResult(
                Success: false,
                ErrorMessage: "UDP + Opus 受信モジュールを利用できません。",
                StatusMessage: "UDP + Opus の受信を開始できませんでした。",
                Diagnostics: GetDiagnostics(),
                ReceiverPort: localPort
            )
        );
    }

    public bool TryReceivePcmFrame(out PcmFrame frame)
    {
        frame = new PcmFrame(0, 0, 0, 0, 0, 0, []);
        return false;
    }

    public void StopListening()
    {
    }

    public ConnectionDiagnostics GetDiagnostics()
    {
        return new ConnectionDiagnostics(
            SelectedCandidatePairType: "udp_opus",
            FailureHint: "native_backend_unavailable",
            NormalizedFailureCode: FailureCode.WebRtcNegotiationFailed
        );
    }

    public BridgeBackendHealth GetBackendHealth()
    {
        return new BridgeBackendHealth(
            IsReady: false,
            IsDevelopmentStub: false,
            Message: $"UDP + Opus 受信モジュールを利用できません。{_startupReason}",
            BlockingFailureCode: FailureCode.WebRtcNegotiationFailed
        );
    }

    public void Close()
    {
    }
}
