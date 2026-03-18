using P2PAudio.Windows.Core.Models;
using P2PAudio.Windows.Core.Networking;
using P2PAudio.Windows.Core.Protocol;

namespace P2PAudio.Windows.App.Services;

public sealed class StubWebRtcBridge : IWebRtcBridge
{
    private readonly bool _enabledForDevelopment;
    private readonly string _startupReason;

    public StubWebRtcBridge(bool enabledForDevelopment, string startupReason)
    {
        _enabledForDevelopment = enabledForDevelopment;
        _startupReason = startupReason;
    }

    public bool IsNativeBackend => false;

    public Task<WebRtcOfferResult> CreateOfferAsync()
    {
        if (!_enabledForDevelopment)
        {
            return Task.FromResult(
                new WebRtcOfferResult(
                    Success: false,
                    ErrorMessage: "ネイティブ接続モジュールが必要です。",
                    SessionId: string.Empty,
                    OfferSdp: string.Empty,
                    Fingerprint: string.Empty,
                    Diagnostics: CreateDiagnostics()
                )
            );
        }

        var sessionId = Guid.NewGuid().ToString();
        return Task.FromResult(
            new WebRtcOfferResult(
                Success: true,
                ErrorMessage: "",
                SessionId: sessionId,
                OfferSdp: "v=0\r\ns=stub-offer\r\n",
                Fingerprint: "stub-fingerprint",
                Diagnostics: CreateDiagnostics()
            )
        );
    }

    public Task<WebRtcAnswerResult> CreateAnswerAsync(string offerSdp)
    {
        if (!_enabledForDevelopment)
        {
            return Task.FromResult(
                new WebRtcAnswerResult(
                    Success: false,
                    ErrorMessage: "ネイティブ接続モジュールが必要です。",
                    AnswerSdp: string.Empty,
                    Fingerprint: string.Empty,
                    Diagnostics: CreateDiagnostics()
                )
            );
        }

        _ = offerSdp;
        return Task.FromResult(
            new WebRtcAnswerResult(
                Success: true,
                ErrorMessage: "",
                AnswerSdp: "v=0\r\ns=stub-answer\r\n",
                Fingerprint: "stub-fingerprint",
                Diagnostics: CreateDiagnostics()
            )
        );
    }

    public Task<WebRtcOperationResult> ApplyAnswerAsync(string answerSdp)
    {
        _ = answerSdp;
        return Task.FromResult(
            new WebRtcOperationResult(
                Success: false,
                ErrorMessage: "ネイティブ接続モジュールを利用できません。",
                StatusMessage: "応答データの適用に失敗しました。",
                Diagnostics: CreateDiagnostics()
            )
        );
    }

    public bool SendPcmPacket(byte[] packet)
    {
        _ = packet;
        return false;
    }

    public bool TryReceivePcmPacket(out byte[] packet)
    {
        packet = [];
        return false;
    }

    public ConnectionDiagnostics GetDiagnostics()
    {
        return CreateDiagnostics();
    }

    public BridgeBackendHealth GetBackendHealth()
    {
        if (_enabledForDevelopment)
        {
            return new BridgeBackendHealth(
                IsReady: true,
                IsDevelopmentStub: true,
                Message: "ネイティブ接続モジュールが見つからないため、開発用スタブで起動しています。",
                BlockingFailureCode: null
            );
        }

        return new BridgeBackendHealth(
            IsReady: false,
            IsDevelopmentStub: false,
            Message: $"ネイティブ接続モジュールを利用できません。{_startupReason}",
            BlockingFailureCode: FailureCode.WebRtcNegotiationFailed
        );
    }

    private ConnectionDiagnostics CreateDiagnostics()
    {
        const string failureHint = "native_backend_unavailable";
        return new ConnectionDiagnostics(
            PathType: UsbTetheringDetector.ClassifyPrimaryPath(),
            LocalCandidatesCount: 0,
            SelectedCandidatePairType: "",
            FailureHint: failureHint,
            NormalizedFailureCode: FailureCodeMapper.FromFailureHint(failureHint)
        );
    }

    public void Close()
    {
    }
}
