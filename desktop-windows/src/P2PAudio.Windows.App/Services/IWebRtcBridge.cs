using P2PAudio.Windows.Core.Models;

namespace P2PAudio.Windows.App.Services;

public interface IWebRtcBridge
{
    Task<WebRtcOfferResult> CreateOfferAsync();
    Task<WebRtcAnswerResult> CreateAnswerAsync(string offerSdp);
    Task<WebRtcOperationResult> ApplyAnswerAsync(string answerSdp);
    bool SendPcmPacket(byte[] packet);
    bool TryReceivePcmPacket(out byte[] packet);
    ConnectionDiagnostics GetDiagnostics();
    BridgeBackendHealth GetBackendHealth();
    bool IsNativeBackend { get; }
    void Close();
}

public sealed record BridgeBackendHealth(
    bool IsReady,
    bool IsDevelopmentStub,
    string Message,
    FailureCode? BlockingFailureCode
);

public sealed record WebRtcOfferResult(
    bool Success,
    string ErrorMessage,
    string SessionId,
    string OfferSdp,
    string Fingerprint,
    ConnectionDiagnostics Diagnostics
);

public sealed record WebRtcAnswerResult(
    bool Success,
    string ErrorMessage,
    string AnswerSdp,
    string Fingerprint,
    ConnectionDiagnostics Diagnostics
);

public sealed record WebRtcOperationResult(
    bool Success,
    string ErrorMessage,
    string StatusMessage,
    ConnectionDiagnostics Diagnostics
);
