using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using P2PAudio.Windows.App.Services;
using P2PAudio.Windows.Core.Models;
using P2PAudio.Windows.Core.Networking;
using P2PAudio.Windows.Core.Protocol;

namespace P2PAudio.Windows.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IWebRtcBridge _bridge;
    private readonly LoopbackPcmSender _loopbackSender;
    private readonly PcmPlaybackService _playbackService;
    private readonly CancellationTokenSource _receiveLoopCts = new();
    private readonly BridgeBackendHealth _backendHealth;

    private string _localSenderFingerprint = string.Empty;
    private string _pendingAnswerSdp = string.Empty;
    private SetupStep _setupStep = SetupStep.Entry;
    private int _receiveIdleTicks;

    public MainViewModel() : this(CreateBridge())
    {
    }

    public MainViewModel(IWebRtcBridge bridge)
    {
        _bridge = bridge;
        _loopbackSender = new LoopbackPcmSender(packet => _bridge.SendPcmPacket(packet));
        _playbackService = new PcmPlaybackService();
        _backendHealth = _bridge.GetBackendHealth();

        BackendLabel = _bridge.IsNativeBackend
            ? "Backend: Native bridge (required)"
            : _backendHealth.IsDevelopmentStub
                ? "Backend: Development stub (ALLOW_STUB_FOR_DEV)"
                : "Backend: Native backend missing";

        UpdateDiagnostics(new ConnectionDiagnostics(PathType: UsbTetheringDetector.ClassifyPrimaryPath()));
        RefreshDiagnosticsFromBridge();

        if (_backendHealth.IsReady)
        {
            StatusMessage = _backendHealth.Message;
            SetStreamState(StreamState.Idle);
        }
        else
        {
            SetFailureState(
                _backendHealth.Message,
                _backendHealth.BlockingFailureCode ?? FailureCode.WebRtcNegotiationFailed
            );
        }

        _ = Task.Run(() => ReceiveLoopAsync(_receiveLoopCts.Token));
    }

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private string currentPayload = string.Empty;

    [ObservableProperty]
    private BitmapImage? payloadQrImage;

    [ObservableProperty]
    private string networkPathLabel = "Network path: Unknown";

    [ObservableProperty]
    private string candidateCountLabel = "Local host ICE candidates: 0";

    [ObservableProperty]
    private string selectedCandidatePairLabel = "Selected pair: -";

    [ObservableProperty]
    private string failureHintLabel = string.Empty;

    [ObservableProperty]
    private string failureCodeLabel = "Failure code: -";

    [ObservableProperty]
    private string verificationCode = string.Empty;

    [ObservableProperty]
    private string activeSessionId = string.Empty;

    [ObservableProperty]
    private string backendLabel = string.Empty;

    [ObservableProperty]
    private string flowStateLabel = "Step: Entry";

    [ObservableProperty]
    private string streamStateLabel = "Stream state: idle";

    [ObservableProperty]
    private StreamState currentStreamState = StreamState.Idle;

    [ObservableProperty]
    private bool isVerificationPending;

    public async Task StartSenderAsync()
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        StopLoopbackIfRunning();
        SetStreamState(StreamState.Capturing);
        SetFailureCode(null, clearWhenNull: true);
        StatusMessage = "Preparing sender offer...";
        FlowStateLabel = "Step: Sender generate init";
        VerificationCode = string.Empty;
        IsVerificationPending = false;
        _setupStep = SetupStep.SenderShowInit;
        UpdateDiagnostics(new ConnectionDiagnostics(PathType: UsbTetheringDetector.ClassifyPrimaryPath()));

        var localOffer = await _bridge.CreateOfferAsync();
        UpdateDiagnostics(localOffer.Diagnostics);
        if (!localOffer.Success)
        {
            SetFailureState(
                $"Create offer failed: {localOffer.ErrorMessage}",
                localOffer.Diagnostics.NormalizedFailureCode ?? FailureCode.WebRtcNegotiationFailed
            );
            return;
        }

        _localSenderFingerprint = localOffer.Fingerprint;
        ActiveSessionId = localOffer.SessionId;
        var payload = PairingInitPayload.Create(
            sessionId: localOffer.SessionId,
            senderDeviceName: Environment.MachineName,
            senderPubKeyFingerprint: localOffer.Fingerprint,
            offerSdp: localOffer.OfferSdp,
            expiresAtUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000
        );
        CurrentPayload = QrPayloadCodec.EncodeInit(payload);
        PayloadQrImage = await QrImageService.CreateAsync(CurrentPayload);
        StatusMessage = "Init payload generated. Share this QR with listener.";
        FlowStateLabel = "Step: Sender show init";
    }

    public void StartListener()
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        _bridge.Close();
        StopLoopbackIfRunning();
        _playbackService.Stop();
        _pendingAnswerSdp = string.Empty;
        _localSenderFingerprint = string.Empty;
        CurrentPayload = string.Empty;
        PayloadQrImage = null;
        VerificationCode = string.Empty;
        ActiveSessionId = string.Empty;
        IsVerificationPending = false;
        _setupStep = SetupStep.ListenerScanInit;
        FlowStateLabel = "Step: Listener scan init";
        StatusMessage = "Ready to scan sender init payload.";
        SetStreamState(StreamState.Idle);
        SetFailureCode(null, clearWhenNull: true);
        UpdateDiagnostics(new ConnectionDiagnostics(PathType: UsbTetheringDetector.ClassifyPrimaryPath()));
        RefreshDiagnosticsFromBridge();
    }

    public async Task ProcessInputPayloadAsync(string rawPayload)
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            StatusMessage = "Payload is empty.";
            return;
        }

        if (_setupStep is SetupStep.SenderShowInit or SetupStep.SenderVerifyCode)
        {
            await PrepareConfirmForVerificationAsync(rawPayload);
            return;
        }

        if (_setupStep == SetupStep.ListenerShowConfirm)
        {
            StatusMessage = "Confirm payload already generated. Wait for stream or press Stop to restart.";
            return;
        }

        _setupStep = SetupStep.ListenerScanInit;
        FlowStateLabel = "Step: Listener scan init";
        await CreateConfirmFromInitAsync(rawPayload);
    }

    public async Task PasteFromClipboardAsync()
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        var dataPackage = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        if (!dataPackage.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            StatusMessage = "Clipboard has no text payload.";
            return;
        }
        var text = await dataPackage.GetTextAsync();
        await ProcessInputPayloadAsync(text);
    }

    public async Task ScanFromCameraAsync()
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        var payload = await QrCameraScannerService.ScanAsync();
        if (string.IsNullOrWhiteSpace(payload))
        {
            StatusMessage = "No QR payload detected.";
            return;
        }
        await ProcessInputPayloadAsync(payload);
    }

    public async Task ApproveVerificationAndConnectAsync()
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_pendingAnswerSdp))
        {
            StatusMessage = "No pending answer to apply.";
            return;
        }

        SetStreamState(StreamState.Connecting);
        var applyResult = await _bridge.ApplyAnswerAsync(_pendingAnswerSdp);
        UpdateDiagnostics(applyResult.Diagnostics);
        if (!applyResult.Success)
        {
            SetFailureState(
                $"Apply answer failed: {applyResult.ErrorMessage}",
                applyResult.Diagnostics.NormalizedFailureCode ?? FailureCode.WebRtcNegotiationFailed
            );
            return;
        }

        IsVerificationPending = false;
        FlowStateLabel = "Step: Streaming";
        SetStreamState(StreamState.Streaming);
        StatusMessage = "Connected. Starting loopback audio sender.";
        RefreshDiagnosticsFromBridge();

        try
        {
            _loopbackSender.Start();
        }
        catch (Exception ex)
        {
            SetFailureState($"Audio capture failed: {ex.Message}", FailureCode.AudioCaptureNotSupported);
        }
    }

    public void RejectVerificationAndRestart()
    {
        RestartSetup("Verification mismatch.", FailureCode.InvalidPayload);
    }

    public void Stop()
    {
        _bridge.Close();
        StopLoopbackIfRunning();
        _playbackService.Stop();

        _pendingAnswerSdp = string.Empty;
        _localSenderFingerprint = string.Empty;
        CurrentPayload = string.Empty;
        PayloadQrImage = null;
        VerificationCode = string.Empty;
        ActiveSessionId = string.Empty;
        IsVerificationPending = false;
        _setupStep = SetupStep.Entry;
        FlowStateLabel = "Step: Entry";
        StatusMessage = "Stopped";
        SetFailureCode(null, clearWhenNull: true);
        SetStreamState(StreamState.Ended);
        RefreshDiagnosticsFromBridge();
    }

    public void Shutdown()
    {
        if (!_receiveLoopCts.IsCancellationRequested)
        {
            _receiveLoopCts.Cancel();
        }
        Stop();
    }

    private async Task CreateConfirmFromInitAsync(string initPayloadRaw)
    {
        try
        {
            var initPayload = QrPayloadCodec.DecodeInit(initPayloadRaw);
            var failure = PairingPayloadValidator.ValidateInit(initPayload, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (failure is not null)
            {
                RestartSetup(failure.Message, failure.Code);
                return;
            }

            SetStreamState(StreamState.Connecting);
            var answer = await _bridge.CreateAnswerAsync(initPayload.OfferSdp);
            UpdateDiagnostics(answer.Diagnostics);
            if (!answer.Success)
            {
                SetFailureState(
                    $"Create answer failed: {answer.ErrorMessage}",
                    answer.Diagnostics.NormalizedFailureCode ?? FailureCode.WebRtcNegotiationFailed
                );
                return;
            }

            var confirmPayload = PairingConfirmPayload.Create(
                sessionId: initPayload.SessionId,
                receiverDeviceName: Environment.MachineName,
                receiverPubKeyFingerprint: answer.Fingerprint,
                answerSdp: answer.AnswerSdp,
                expiresAtUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000
            );
            CurrentPayload = QrPayloadCodec.EncodeConfirm(confirmPayload);
            PayloadQrImage = await QrImageService.CreateAsync(CurrentPayload);
            VerificationCode = P2PAudio.Windows.Core.Protocol.VerificationCode.FromSessionAndFingerprints(
                sessionId: initPayload.SessionId,
                senderFingerprint: initPayload.SenderPubKeyFingerprint,
                receiverFingerprint: answer.Fingerprint
            );
            ActiveSessionId = initPayload.SessionId;
            _setupStep = SetupStep.ListenerShowConfirm;
            FlowStateLabel = "Step: Listener show confirm";
            StatusMessage = "Confirm payload generated. Show this QR to sender, then wait for stream.";
            SetFailureCode(null, clearWhenNull: true);
            RefreshDiagnosticsFromBridge();
        }
        catch (SessionFailure failure)
        {
            RestartSetup(failure.Message, failure.Code);
        }
        catch (Exception ex)
        {
            RestartSetup($"Failed to process init payload: {ex.Message}", FailureCode.InvalidPayload);
        }
    }

    private Task PrepareConfirmForVerificationAsync(string confirmPayloadRaw)
    {
        try
        {
            var confirmPayload = QrPayloadCodec.DecodeConfirm(confirmPayloadRaw);
            var failure = PairingPayloadValidator.ValidateConfirm(
                confirmPayload,
                expectedSessionId: ActiveSessionId,
                nowUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
            if (failure is not null)
            {
                RestartSetup(failure.Message, failure.Code);
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(_localSenderFingerprint))
            {
                RestartSetup("Local sender fingerprint is missing.", FailureCode.InvalidPayload);
                return Task.CompletedTask;
            }

            VerificationCode = P2PAudio.Windows.Core.Protocol.VerificationCode.FromSessionAndFingerprints(
                sessionId: ActiveSessionId,
                senderFingerprint: _localSenderFingerprint,
                receiverFingerprint: confirmPayload.ReceiverPubKeyFingerprint
            );
            _pendingAnswerSdp = confirmPayload.AnswerSdp;
            _setupStep = SetupStep.SenderVerifyCode;
            IsVerificationPending = true;
            FlowStateLabel = "Step: Sender verify code";
            SetStreamState(StreamState.Connecting);
            SetFailureCode(null, clearWhenNull: true);
            StatusMessage = "Verify the 6-digit code and approve.";
        }
        catch (SessionFailure failure)
        {
            RestartSetup(failure.Message, failure.Code);
        }
        catch (Exception ex)
        {
            RestartSetup($"Failed to process confirm payload: {ex.Message}", FailureCode.InvalidPayload);
        }
        return Task.CompletedTask;
    }

    private bool EnsureBackendReady()
    {
        if (_backendHealth.IsReady)
        {
            return true;
        }

        SetFailureState(
            $"Native backend is required. {_backendHealth.Message}",
            _backendHealth.BlockingFailureCode ?? FailureCode.WebRtcNegotiationFailed
        );
        return false;
    }

    private void RestartSetup(string message, FailureCode code)
    {
        _bridge.Close();
        StopLoopbackIfRunning();
        _playbackService.Stop();
        _pendingAnswerSdp = string.Empty;
        _localSenderFingerprint = string.Empty;
        CurrentPayload = string.Empty;
        PayloadQrImage = null;
        VerificationCode = string.Empty;
        ActiveSessionId = string.Empty;
        IsVerificationPending = false;
        _setupStep = SetupStep.Entry;
        FlowStateLabel = "Step: Entry";
        SetStreamState(StreamState.Idle);
        SetFailureCode(code);
        StatusMessage = $"{message} Restart from Step 1.";
    }

    private void SetFailureState(string message, FailureCode code)
    {
        SetStreamState(StreamState.Failed);
        SetFailureCode(code);
        StatusMessage = message;
    }

    private void SetInterruptedState(string message, FailureCode code)
    {
        if (CurrentStreamState == StreamState.Failed || CurrentStreamState == StreamState.Ended)
        {
            return;
        }

        SetStreamState(StreamState.Interrupted);
        SetFailureCode(code);
        StatusMessage = message;
    }

    private void SetFailureCode(FailureCode? code, bool clearWhenNull = false)
    {
        if (code is not null)
        {
            FailureCodeLabel = $"Failure code: {FailureCodeMapper.ToWireValue(code.Value)}";
            return;
        }

        if (clearWhenNull)
        {
            FailureCodeLabel = "Failure code: -";
        }
    }

    private void SetStreamState(StreamState state)
    {
        CurrentStreamState = state;
        StreamStateLabel = $"Stream state: {state.ToString().ToLowerInvariant()}";
    }

    private void StopLoopbackIfRunning()
    {
        if (_loopbackSender.IsRunning)
        {
            _loopbackSender.Stop();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_bridge.TryReceivePcmPacket(out var packet))
                {
                    _receiveIdleTicks = 0;
                    _playbackService.PlayPacket(packet);
                    if (CurrentStreamState == StreamState.Connecting && _setupStep == SetupStep.ListenerShowConfirm)
                    {
                        SetStreamState(StreamState.Streaming);
                        FlowStateLabel = "Step: Streaming";
                        StatusMessage = "Streaming remote audio.";
                    }
                    else if (CurrentStreamState == StreamState.Interrupted)
                    {
                        SetStreamState(StreamState.Streaming);
                        StatusMessage = "Stream recovered.";
                    }
                    continue;
                }

                _receiveIdleTicks++;
                if (_receiveIdleTicks % 25 == 0)
                {
                    EvaluateStreamHealth();
                }

                await Task.Delay(20, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetFailureState($"Receive loop failed: {ex.Message}", FailureCode.WebRtcNegotiationFailed);
        }
    }

    private void EvaluateStreamHealth()
    {
        try
        {
            var diagnostics = _bridge.GetDiagnostics();
            UpdateDiagnostics(diagnostics);

            var code = diagnostics.NormalizedFailureCode ?? FailureCodeMapper.FromFailureHint(diagnostics.FailureHint);
            if (code is not null)
            {
                if (code == FailureCode.NetworkChanged || code == FailureCode.PeerUnreachable)
                {
                    SetInterruptedState($"Stream interrupted: {diagnostics.FailureHint}", code.Value);
                }
                else if (code == FailureCode.WebRtcNegotiationFailed && CurrentStreamState == StreamState.Streaming)
                {
                    SetInterruptedState($"Transport unstable: {diagnostics.FailureHint}", code.Value);
                }
            }
        }
        catch
        {
            // Diagnostics collection should not terminate the receive loop.
        }
    }

    private void RefreshDiagnosticsFromBridge()
    {
        try
        {
            var diagnostics = _bridge.GetDiagnostics();
            if (diagnostics.PathType != NetworkPathType.Unknown ||
                diagnostics.LocalCandidatesCount > 0 ||
                !string.IsNullOrWhiteSpace(diagnostics.SelectedCandidatePairType) ||
                !string.IsNullOrWhiteSpace(diagnostics.FailureHint))
            {
                UpdateDiagnostics(diagnostics);
                return;
            }
        }
        catch
        {
        }

        UpdateDiagnostics(new ConnectionDiagnostics(PathType: UsbTetheringDetector.ClassifyPrimaryPath()));
    }

    private void UpdateDiagnostics(ConnectionDiagnostics diagnostics)
    {
        NetworkPathLabel = diagnostics.PathType switch
        {
            NetworkPathType.WifiLan => "Network path: Wi-Fi / LAN",
            NetworkPathType.UsbTether => "Network path: USB tethering",
            _ => "Network path: Unknown"
        };
        CandidateCountLabel = $"Local host ICE candidates: {diagnostics.LocalCandidatesCount}";
        SelectedCandidatePairLabel = string.IsNullOrWhiteSpace(diagnostics.SelectedCandidatePairType)
            ? "Selected pair: -"
            : $"Selected pair: {diagnostics.SelectedCandidatePairType}";
        FailureHintLabel = string.IsNullOrWhiteSpace(diagnostics.FailureHint)
            ? string.Empty
            : $"Hint: {diagnostics.FailureHint}";

        var mappedCode = diagnostics.NormalizedFailureCode ?? FailureCodeMapper.FromFailureHint(diagnostics.FailureHint);
        if (mappedCode is not null)
        {
            SetFailureCode(mappedCode.Value);
        }
    }

    private static IWebRtcBridge CreateBridge()
    {
        try
        {
            return new NativeWebRtcBridge();
        }
        catch (Exception ex)
        {
            return new StubWebRtcBridge(
                enabledForDevelopment: IsStubAllowedForDevelopment(),
                startupReason: ex.Message
            );
        }
    }

    private static bool IsStubAllowedForDevelopment()
    {
        var value = Environment.GetEnvironmentVariable("ALLOW_STUB_FOR_DEV");
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value, "1", StringComparison.Ordinal) ||
               (bool.TryParse(value, out var enabled) && enabled);
    }

    private enum SetupStep
    {
        Entry,
        ListenerScanInit,
        SenderShowInit,
        SenderVerifyCode,
        ListenerShowConfirm
    }
}
