using P2PAudio.Windows.App.Services;
using P2PAudio.Windows.App.ViewModels;
using P2PAudio.Windows.Core.Models;
using P2PAudio.Windows.Core.Protocol;

namespace P2PAudio.Windows.App.Tests;

public sealed class MainViewModelTests
{
    private static long FutureExpiry => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000;
    private static long PastExpiry => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1;

    private static string MakeInitPayload(string sessionId = "session-1", long? expiresAtUnixMs = null) =>
        QrPayloadCodec.EncodeInit(PairingInitPayload.Create(
            sessionId: sessionId,
            senderDeviceName: "sender",
            senderPubKeyFingerprint: "sender-fp",
            offerSdp: "v=0\r\ns=offer\r\n",
            expiresAtUnixMs: expiresAtUnixMs ?? FutureExpiry
        ));

    private static string MakeConfirmPayload(string sessionId = "session-1", long? expiresAtUnixMs = null) =>
        QrPayloadCodec.EncodeConfirm(PairingConfirmPayload.Create(
            sessionId: sessionId,
            receiverDeviceName: "receiver",
            receiverPubKeyFingerprint: "receiver-fp",
            answerSdp: "v=0\r\ns=answer\r\n",
            expiresAtUnixMs: expiresAtUnixMs ?? FutureExpiry
        ));

    // --- Backend readiness ---

    [Fact]
    public async Task StartSender_WhenBackendIsNotReady_StaysFailed()
    {
        var bridge = new FakeWebRtcBridge
        {
            BackendHealth = new BridgeBackendHealth(
                IsReady: false,
                IsDevelopmentStub: false,
                Message: "Native runtime missing.",
                BlockingFailureCode: FailureCode.WebRtcNegotiationFailed
            )
        };
        var viewModel = new MainViewModel(bridge);

        await viewModel.StartSenderAsync();

        Assert.Equal(StreamState.Failed, viewModel.CurrentStreamState);
        Assert.Contains("Native backend is required", viewModel.StatusMessage);
        Assert.Contains("webrtc_negotiation_failed", viewModel.FailureCodeLabel);
        viewModel.Shutdown();
    }

    [Fact]
    public void StartListener_WhenBackendIsNotReady_StaysFailed()
    {
        var bridge = new FakeWebRtcBridge
        {
            BackendHealth = new BridgeBackendHealth(
                IsReady: false,
                IsDevelopmentStub: false,
                Message: "Native runtime missing.",
                BlockingFailureCode: FailureCode.WebRtcNegotiationFailed
            )
        };
        var viewModel = new MainViewModel(bridge);

        viewModel.StartListener();

        Assert.Equal(StreamState.Failed, viewModel.CurrentStreamState);
        Assert.Contains("Native backend is required", viewModel.StatusMessage);
        viewModel.Shutdown();
    }

    // --- Invalid / malformed payloads ---

    [Fact]
    public async Task ProcessInputPayload_InvalidPayload_RestartsToEntry()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        await viewModel.ProcessInputPayloadAsync("p2paudio-z1:invalid");

        Assert.Equal("Step: Entry", viewModel.FlowStateLabel);
        Assert.Equal(StreamState.Idle, viewModel.CurrentStreamState);
        Assert.Contains("invalid_payload", viewModel.FailureCodeLabel);
        viewModel.Shutdown();
    }

    [Fact]
    public async Task ProcessInputPayload_EmptyString_NoStateChange()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        await viewModel.ProcessInputPayloadAsync("");

        Assert.Equal("Step: Entry", viewModel.FlowStateLabel);
        Assert.Equal(StreamState.Idle, viewModel.CurrentStreamState);
        Assert.Contains("empty", viewModel.StatusMessage);
        viewModel.Shutdown();
    }

    // --- Expired payload ---

    [Fact]
    public async Task ProcessInputPayload_ExpiredInitPayload_RestartsToEntry()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);
        viewModel.StartListener();

        var expired = MakeInitPayload(expiresAtUnixMs: PastExpiry);
        await viewModel.ProcessInputPayloadAsync(expired);

        Assert.Equal("Step: Entry", viewModel.FlowStateLabel);
        Assert.Equal(StreamState.Idle, viewModel.CurrentStreamState);
        Assert.Contains("session_expired", viewModel.FailureCodeLabel);
        viewModel.Shutdown();
    }

    [Fact]
    public async Task ProcessInputPayload_ExpiredConfirmPayload_RestartsToEntry()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        await viewModel.StartSenderAsync();
        Assert.Equal("Step: Sender show init", viewModel.FlowStateLabel);

        var expired = MakeConfirmPayload(expiresAtUnixMs: PastExpiry);
        await viewModel.ProcessInputPayloadAsync(expired);

        Assert.Equal("Step: Entry", viewModel.FlowStateLabel);
        Assert.Equal(StreamState.Idle, viewModel.CurrentStreamState);
        Assert.Contains("session_expired", viewModel.FailureCodeLabel);
        viewModel.Shutdown();
    }

    // --- Session ID mismatch ---

    [Fact]
    public async Task ProcessInputPayload_SessionIdMismatch_RestartsToEntry()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        await viewModel.StartSenderAsync();
        Assert.Equal("Step: Sender show init", viewModel.FlowStateLabel);

        var mismatch = MakeConfirmPayload(sessionId: "different-session");
        await viewModel.ProcessInputPayloadAsync(mismatch);

        Assert.Equal("Step: Entry", viewModel.FlowStateLabel);
        Assert.Equal(StreamState.Idle, viewModel.CurrentStreamState);
        Assert.Contains("invalid_payload", viewModel.FailureCodeLabel);
        viewModel.Shutdown();
    }

    // --- Offer / answer failures ---

    [Fact]
    public async Task StartSender_OfferFailure_MarksFailedState()
    {
        var bridge = new FakeWebRtcBridge
        {
            OfferResult = new WebRtcOfferResult(
                Success: false,
                ErrorMessage: "create_peer_connection_failed",
                SessionId: string.Empty,
                OfferSdp: string.Empty,
                Fingerprint: string.Empty,
                Diagnostics: new ConnectionDiagnostics(
                    FailureHint: "create_peer_connection_failed",
                    NormalizedFailureCode: FailureCode.WebRtcNegotiationFailed
                )
            )
        };
        var viewModel = new MainViewModel(bridge);

        await viewModel.StartSenderAsync();

        Assert.Equal(StreamState.Failed, viewModel.CurrentStreamState);
        Assert.Contains("Create offer failed", viewModel.StatusMessage);
        Assert.Contains("webrtc_negotiation_failed", viewModel.FailureCodeLabel);
        viewModel.Shutdown();
    }

    [Fact]
    public async Task ListenerProcessInit_AnswerFailure_MarksFailedState()
    {
        var bridge = new FakeWebRtcBridge
        {
            AnswerResult = new WebRtcAnswerResult(
                Success: false,
                ErrorMessage: "set_remote_offer_failed",
                AnswerSdp: string.Empty,
                Fingerprint: string.Empty,
                Diagnostics: new ConnectionDiagnostics(
                    FailureHint: "set_remote_offer_failed",
                    NormalizedFailureCode: FailureCode.WebRtcNegotiationFailed
                )
            )
        };
        var viewModel = new MainViewModel(bridge);
        viewModel.StartListener();

        await viewModel.ProcessInputPayloadAsync(MakeInitPayload());

        Assert.Equal(StreamState.Failed, viewModel.CurrentStreamState);
        Assert.Contains("Create answer failed", viewModel.StatusMessage);
        Assert.Contains("webrtc_negotiation_failed", viewModel.FailureCodeLabel);
        viewModel.Shutdown();
    }

    [Fact]
    public async Task SenderApplyAnswer_Failure_MarksFailedState()
    {
        var bridge = new FakeWebRtcBridge
        {
            ApplyResult = new WebRtcOperationResult(
                Success: false,
                ErrorMessage: "set_remote_answer_failed",
                StatusMessage: "failed",
                Diagnostics: new ConnectionDiagnostics(
                    FailureHint: "set_remote_answer_failed",
                    NormalizedFailureCode: FailureCode.WebRtcNegotiationFailed
                )
            )
        };
        var viewModel = new MainViewModel(bridge);

        await viewModel.StartSenderAsync();
        await viewModel.ProcessInputPayloadAsync(MakeConfirmPayload());
        Assert.True(viewModel.IsVerificationPending);

        await viewModel.ApproveVerificationAndConnectAsync();

        Assert.Equal(StreamState.Failed, viewModel.CurrentStreamState);
        Assert.Contains("Apply answer failed", viewModel.StatusMessage);
        viewModel.Shutdown();
    }

    // --- Verification reject ---

    [Fact]
    public async Task RejectVerification_RestartsToEntry()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        await viewModel.StartSenderAsync();
        await viewModel.ProcessInputPayloadAsync(MakeConfirmPayload());
        Assert.True(viewModel.IsVerificationPending);
        Assert.Equal("Step: Sender verify code", viewModel.FlowStateLabel);

        viewModel.RejectVerificationAndRestart();

        Assert.Equal("Step: Entry", viewModel.FlowStateLabel);
        Assert.Equal(StreamState.Idle, viewModel.CurrentStreamState);
        Assert.False(viewModel.IsVerificationPending);
        Assert.Contains("invalid_payload", viewModel.FailureCodeLabel);
        viewModel.Shutdown();
    }

    // --- Stop ---

    [Fact]
    public async Task Stop_ResetsAllState()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        await viewModel.StartSenderAsync();
        Assert.Equal("Step: Sender show init", viewModel.FlowStateLabel);
        Assert.NotEmpty(viewModel.CurrentPayload);

        viewModel.Stop();

        Assert.Equal("Step: Entry", viewModel.FlowStateLabel);
        Assert.Equal(StreamState.Ended, viewModel.CurrentStreamState);
        Assert.Empty(viewModel.CurrentPayload);
        Assert.Empty(viewModel.VerificationCode);
        Assert.Empty(viewModel.ActiveSessionId);
        Assert.False(viewModel.IsVerificationPending);
        Assert.Equal("Stopped", viewModel.StatusMessage);
        viewModel.Shutdown();
    }

    // --- Listener full flow ---

    [Fact]
    public async Task ProcessInputPayload_WhenListenerAlreadyHasConfirm_KeepsListenerState()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);
        var initRaw = MakeInitPayload();

        viewModel.StartListener();
        await viewModel.ProcessInputPayloadAsync(initRaw);
        Assert.Equal("Step: Listener show confirm", viewModel.FlowStateLabel);

        await viewModel.ProcessInputPayloadAsync(initRaw);

        Assert.Equal("Step: Listener show confirm", viewModel.FlowStateLabel);
        Assert.Contains("already generated", viewModel.StatusMessage);
        viewModel.Shutdown();
    }

    [Fact]
    public async Task ListenerReceiveLoop_FirstPacketTransitionsConnectingToStreaming()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        viewModel.StartListener();
        await viewModel.ProcessInputPayloadAsync(MakeInitPayload());
        Assert.Equal(StreamState.Connecting, viewModel.CurrentStreamState);

        bridge.EnqueueIncomingPacket([0x00]);
        await WaitUntilAsync(() => viewModel.CurrentStreamState == StreamState.Streaming, TimeSpan.FromSeconds(2));

        Assert.Equal("Step: Streaming", viewModel.FlowStateLabel);
        Assert.Contains("Streaming remote audio", viewModel.StatusMessage);
        viewModel.Shutdown();
    }

    [Fact]
    public async Task ListenerFullFlow_GeneratesVerificationCode()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        viewModel.StartListener();
        await viewModel.ProcessInputPayloadAsync(MakeInitPayload());

        Assert.Equal("Step: Listener show confirm", viewModel.FlowStateLabel);
        Assert.Equal(StreamState.Connecting, viewModel.CurrentStreamState);
        Assert.NotEmpty(viewModel.VerificationCode);
        Assert.Equal(6, viewModel.VerificationCode.Length);
        Assert.NotEmpty(viewModel.CurrentPayload);
        Assert.Equal("session-1", viewModel.ActiveSessionId);
        viewModel.Shutdown();
    }

    // --- Sender full flow ---

    [Fact]
    public async Task SenderFullFlow_OfferThroughVerifyToConnect()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        await viewModel.StartSenderAsync();
        Assert.Equal("Step: Sender show init", viewModel.FlowStateLabel);
        Assert.Equal(StreamState.Capturing, viewModel.CurrentStreamState);
        Assert.NotEmpty(viewModel.CurrentPayload);
        Assert.Equal("session-1", viewModel.ActiveSessionId);

        await viewModel.ProcessInputPayloadAsync(MakeConfirmPayload());
        Assert.Equal("Step: Sender verify code", viewModel.FlowStateLabel);
        Assert.Equal(StreamState.Connecting, viewModel.CurrentStreamState);
        Assert.True(viewModel.IsVerificationPending);
        Assert.NotEmpty(viewModel.VerificationCode);
        Assert.Equal(6, viewModel.VerificationCode.Length);

        await viewModel.ApproveVerificationAndConnectAsync();
        Assert.Equal("Step: Streaming", viewModel.FlowStateLabel);
        Assert.Equal(StreamState.Streaming, viewModel.CurrentStreamState);
        Assert.False(viewModel.IsVerificationPending);
        viewModel.Shutdown();
    }

    [Fact]
    public async Task SenderVerificationCode_MatchesListenerVerificationCode()
    {
        var senderBridge = new FakeWebRtcBridge();
        var listenerBridge = new FakeWebRtcBridge();

        var sender = new MainViewModel(senderBridge);
        var listener = new MainViewModel(listenerBridge);

        await sender.StartSenderAsync();
        listener.StartListener();
        await listener.ProcessInputPayloadAsync(MakeInitPayload());

        await sender.ProcessInputPayloadAsync(MakeConfirmPayload());

        Assert.Equal(sender.VerificationCode, listener.VerificationCode);
        Assert.NotEmpty(sender.VerificationCode);

        sender.Shutdown();
        listener.Shutdown();
    }

    // --- Stream health monitoring ---

    [Fact]
    public async Task StreamHealth_NetworkChanged_TransitionsToInterrupted()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        viewModel.StartListener();
        await viewModel.ProcessInputPayloadAsync(MakeInitPayload());
        bridge.EnqueueIncomingPacket([0x00]);
        await WaitUntilAsync(() => viewModel.CurrentStreamState == StreamState.Streaming, TimeSpan.FromSeconds(2));

        bridge.Diagnostics = new ConnectionDiagnostics(
            FailureHint: "network_changed",
            NormalizedFailureCode: FailureCode.NetworkChanged
        );

        await WaitUntilAsync(() => viewModel.CurrentStreamState == StreamState.Interrupted, TimeSpan.FromSeconds(3));

        Assert.Equal(StreamState.Interrupted, viewModel.CurrentStreamState);
        Assert.Contains("network_changed", viewModel.FailureCodeLabel);
        viewModel.Shutdown();
    }

    [Fact]
    public async Task StreamHealth_InterruptedRecovery_TransitionsBackToStreaming()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        viewModel.StartListener();
        await viewModel.ProcessInputPayloadAsync(MakeInitPayload());
        bridge.EnqueueIncomingPacket([0x00]);
        await WaitUntilAsync(() => viewModel.CurrentStreamState == StreamState.Streaming, TimeSpan.FromSeconds(2));

        bridge.Diagnostics = new ConnectionDiagnostics(
            FailureHint: "peer_unreachable",
            NormalizedFailureCode: FailureCode.PeerUnreachable
        );
        await WaitUntilAsync(() => viewModel.CurrentStreamState == StreamState.Interrupted, TimeSpan.FromSeconds(3));

        bridge.Diagnostics = new ConnectionDiagnostics();
        bridge.EnqueueIncomingPacket([0x01]);
        await WaitUntilAsync(() => viewModel.CurrentStreamState == StreamState.Streaming, TimeSpan.FromSeconds(2));

        Assert.Equal(StreamState.Streaming, viewModel.CurrentStreamState);
        Assert.Contains("recovered", viewModel.StatusMessage);
        viewModel.Shutdown();
    }

    // --- Diagnostics display ---

    [Fact]
    public async Task Diagnostics_UsbTetheringPath_DisplayedCorrectly()
    {
        var bridge = new FakeWebRtcBridge
        {
            Diagnostics = new ConnectionDiagnostics(
                PathType: NetworkPathType.UsbTether,
                LocalCandidatesCount: 2,
                SelectedCandidatePairType: "host"
            )
        };
        var viewModel = new MainViewModel(bridge);

        await viewModel.StartSenderAsync();

        Assert.Contains("USB tethering", viewModel.NetworkPathLabel);
        Assert.Contains("2", viewModel.CandidateCountLabel);
        viewModel.Shutdown();
    }

    [Fact]
    public async Task Diagnostics_WifiLanPath_DisplayedCorrectly()
    {
        var bridge = new FakeWebRtcBridge
        {
            Diagnostics = new ConnectionDiagnostics(
                PathType: NetworkPathType.WifiLan,
                LocalCandidatesCount: 3,
                SelectedCandidatePairType: "host"
            )
        };
        var viewModel = new MainViewModel(bridge);

        await viewModel.StartSenderAsync();

        Assert.Contains("Wi-Fi / LAN", viewModel.NetworkPathLabel);
        Assert.Contains("3", viewModel.CandidateCountLabel);
        Assert.Contains("host", viewModel.SelectedCandidatePairLabel);
        viewModel.Shutdown();
    }

    [Fact]
    public async Task Diagnostics_FailureHint_MappedToFailureCode()
    {
        var bridge = new FakeWebRtcBridge
        {
            Diagnostics = new ConnectionDiagnostics(
                FailureHint: "usb_tether_detected_but_not_reachable"
            )
        };
        var viewModel = new MainViewModel(bridge);

        await viewModel.StartSenderAsync();

        Assert.Contains("usb_tether_detected_but_not_reachable", viewModel.FailureCodeLabel);
        viewModel.Shutdown();
    }

    // --- ApproveVerification without pending answer ---

    [Fact]
    public async Task ApproveVerification_NoPendingAnswer_NoOp()
    {
        var bridge = new FakeWebRtcBridge();
        var viewModel = new MainViewModel(bridge);

        await viewModel.ApproveVerificationAndConnectAsync();

        Assert.Contains("No pending answer", viewModel.StatusMessage);
        viewModel.Shutdown();
    }

    // --- Helpers ---

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate());
    }

    private sealed class FakeWebRtcBridge : IWebRtcBridge
    {
        private readonly Queue<byte[]> _incomingPackets = new();
        private readonly object _sync = new();

        public WebRtcOfferResult OfferResult { get; set; } = new(
            Success: true,
            ErrorMessage: string.Empty,
            SessionId: "session-1",
            OfferSdp: "v=0\r\ns=offer\r\n",
            Fingerprint: "sender-fp",
            Diagnostics: new ConnectionDiagnostics()
        );

        public WebRtcAnswerResult AnswerResult { get; set; } = new(
            Success: true,
            ErrorMessage: string.Empty,
            AnswerSdp: "v=0\r\ns=answer\r\n",
            Fingerprint: "receiver-fp",
            Diagnostics: new ConnectionDiagnostics()
        );

        public WebRtcOperationResult ApplyResult { get; set; } = new(
            Success: true,
            ErrorMessage: string.Empty,
            StatusMessage: "ok",
            Diagnostics: new ConnectionDiagnostics()
        );

        public BridgeBackendHealth BackendHealth { get; set; } = new(
            IsReady: true,
            IsDevelopmentStub: false,
            Message: "Native backend ready.",
            BlockingFailureCode: null
        );

        public ConnectionDiagnostics Diagnostics { get; set; } = new();

        public bool IsNativeBackend => true;

        public Task<WebRtcOfferResult> CreateOfferAsync() => Task.FromResult(OfferResult);

        public Task<WebRtcAnswerResult> CreateAnswerAsync(string offerSdp)
        {
            _ = offerSdp;
            return Task.FromResult(AnswerResult);
        }

        public Task<WebRtcOperationResult> ApplyAnswerAsync(string answerSdp)
        {
            _ = answerSdp;
            return Task.FromResult(ApplyResult);
        }

        public bool SendPcmPacket(byte[] packet)
        {
            _ = packet;
            return true;
        }

        public bool TryReceivePcmPacket(out byte[] packet)
        {
            lock (_sync)
            {
                if (_incomingPackets.Count > 0)
                {
                    packet = _incomingPackets.Dequeue();
                    return true;
                }
            }

            packet = [];
            return false;
        }

        public ConnectionDiagnostics GetDiagnostics() => Diagnostics;

        public BridgeBackendHealth GetBackendHealth() => BackendHealth;

        public void Close()
        {
        }

        public void EnqueueIncomingPacket(byte[] packet)
        {
            lock (_sync)
            {
                _incomingPackets.Enqueue(packet);
            }
        }
    }
}
