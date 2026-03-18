using System.Runtime.ExceptionServices;
using CommunityToolkit.Mvvm.ComponentModel;
using P2PAudio.Windows.App.Services;
using P2PAudio.Windows.Core.Models;
using P2PAudio.Windows.Core.Networking;
using P2PAudio.Windows.Core.Protocol;

namespace P2PAudio.Windows.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const long PayloadTtlMs = 600_000;

    private IWebRtcBridge _bridge;
    private readonly Func<IWebRtcBridge>? _startupBridgeFactory;
    private readonly IConnectionCodeSessionFactory _connectionCodeSessionFactory;
    private readonly LoopbackPcmSender _loopbackSender;
    private readonly PcmPlaybackService _playbackService;
    private readonly CancellationTokenSource _receiveLoopCts = new();
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private BridgeBackendHealth _backendHealth;
    private Task? _startupTask;
    private bool _isStartupComplete;
    private bool _receiveLoopStarted;

    private string _localSenderFingerprint = string.Empty;
    private string _pendingAnswerSdp = string.Empty;
    private int _receiveIdleTicks;
    private IConnectionCodeSession? _connectionCodeSession;
    private CancellationTokenSource? _connectionCodeWaitCts;

    public MainViewModel() : this(initializeImmediately: true)
    {
    }

    public MainViewModel(bool initializeImmediately)
        : this(
            initialBridge: initializeImmediately ? CreateBridge() : CreateStartupPlaceholderBridge(),
            startupBridgeFactory: initializeImmediately ? null : CreateBridge,
            connectionCodeSessionFactory: null,
            initializeImmediately: initializeImmediately
        )
    {
    }

    public MainViewModel(IWebRtcBridge bridge) : this(
        bridge,
        connectionCodeSessionFactory: null,
        initializeImmediately: true
    )
    {
    }

    public MainViewModel(IWebRtcBridge bridge, bool initializeImmediately)
        : this(
            bridge,
            connectionCodeSessionFactory: null,
            initializeImmediately: initializeImmediately
        )
    {
    }

    public MainViewModel(
        IWebRtcBridge bridge,
        IConnectionCodeSessionFactory? connectionCodeSessionFactory,
        bool initializeImmediately)
        : this(
            initialBridge: initializeImmediately ? bridge : CreateStartupPlaceholderBridge(),
            startupBridgeFactory: initializeImmediately ? null : () => bridge,
            connectionCodeSessionFactory: connectionCodeSessionFactory,
            initializeImmediately: initializeImmediately
        )
    {
    }

    private MainViewModel(
        IWebRtcBridge initialBridge,
        Func<IWebRtcBridge>? startupBridgeFactory,
        IConnectionCodeSessionFactory? connectionCodeSessionFactory,
        bool initializeImmediately)
    {
        _bridge = initialBridge;
        _startupBridgeFactory = startupBridgeFactory;
        _connectionCodeSessionFactory = connectionCodeSessionFactory ?? new ConnectionCodeSessionFactory();
        _loopbackSender = new LoopbackPcmSender(packet => _bridge.SendPcmPacket(packet));
        _playbackService = new PcmPlaybackService();
        _backendHealth = CreatePendingBackendHealth();

        BackendLabel = "内部処理を初期化しています...";
        StatusMessage = "起動準備をしています。";
        UpdateDiagnostics(new ConnectionDiagnostics(PathType: UsbTetheringDetector.ClassifyPrimaryPath()));

        if (initializeImmediately)
        {
            ApplyStartupState(CreateStartupState(initialBridge));
        }
    }

    [ObservableProperty]
    private string statusMessage = "準備できました。";

    [ObservableProperty]
    private string currentPayload = string.Empty;

    [ObservableProperty]
    private string currentConnectionCode = string.Empty;

    [ObservableProperty]
    private string networkPathLabel = "接続経路: 判定前";

    [ObservableProperty]
    private string candidateCountLabel = "利用可能なローカル候補: 0";

    [ObservableProperty]
    private string selectedCandidatePairLabel = "選択中の接続経路: -";

    [ObservableProperty]
    private string failureHintLabel = string.Empty;

    [ObservableProperty]
    private string failureCodeLabel = "原因コード: -";

    [ObservableProperty]
    private string verificationCode = string.Empty;

    public string VerificationCodeDisplay => FormatVerificationCode(VerificationCode);

    [ObservableProperty]
    private string activeSessionId = string.Empty;

    [ObservableProperty]
    private string backendLabel = string.Empty;

    [ObservableProperty]
    private string flowStateLabel = "案内: 最初の選択";

    [ObservableProperty]
    private string streamStateLabel = "待機中";

    [ObservableProperty]
    private StreamState currentStreamState = StreamState.Idle;

    [ObservableProperty]
    private bool isVerificationPending;

    [ObservableProperty]
    private SetupStep currentSetupStep = SetupStep.Entry;

    public string RecommendedAction => GetRecommendedAction();

    public double ProgressValue => CurrentSetupStep switch
    {
        SetupStep.Entry => 1,
        SetupStep.PathDiagnosing => 2,
        SetupStep.ListenerScanInit => 2,
        SetupStep.SenderShowInit => 2,
        SetupStep.SenderVerifyCode => 3,
        SetupStep.ListenerShowConfirm => 3,
        _ => 1
    };

    public string StreamStateIndicator => CurrentStreamState switch
    {
        StreamState.Idle => "\u23F8",
        StreamState.Capturing => "\u23F3",
        StreamState.Connecting => "\uD83D\uDD04",
        StreamState.Streaming => "\uD83D\uDFE2",
        StreamState.Interrupted => "\u26A0\uFE0F",
        StreamState.Failed => "\u274C",
        StreamState.Ended => "\u23F9",
        _ => "\u23F8"
    };

    public string StepProgressLabel => CurrentSetupStep switch
    {
        SetupStep.Entry => "\u2460 \u5F79\u5272\u3092\u9078\u3076",
        SetupStep.PathDiagnosing => "\u2460 \u6E96\u5099\u4E2D",
        SetupStep.SenderShowInit => "\u2461 \u30C7\u30FC\u30BF\u3092\u5171\u6709",
        SetupStep.ListenerScanInit => "\u2461 \u30C7\u30FC\u30BF\u3092\u5165\u529B",
        SetupStep.SenderVerifyCode => "\u2462 \u30B3\u30FC\u30C9\u78BA\u8A8D",
        SetupStep.ListenerShowConfirm => "\u2462 \u30B3\u30FC\u30C9\u78BA\u8A8D",
        _ => string.Empty
    };

    public bool CanStartSender => _backendHealth.IsReady &&
        (CurrentSetupStep == SetupStep.Entry || CurrentStreamState == StreamState.Failed) &&
        CurrentStreamState is StreamState.Idle or StreamState.Ended or StreamState.Failed;

    public bool CanStartListener => _backendHealth.IsReady &&
        (CurrentSetupStep == SetupStep.Entry || CurrentStreamState == StreamState.Failed) &&
        CurrentStreamState is StreamState.Idle or StreamState.Ended or StreamState.Failed;

    public bool CanProcessPayload => _backendHealth.IsReady &&
        CurrentSetupStep is SetupStep.ListenerScanInit or SetupStep.SenderShowInit or SetupStep.SenderVerifyCode;

    public bool CanApproveCode => _backendHealth.IsReady && IsVerificationPending;

    public bool CanRejectCode => IsVerificationPending;

    public bool CanStop => CurrentSetupStep != SetupStep.Entry || CurrentStreamState != StreamState.Idle;

    public async Task StartSenderAsync()
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        ResetConnectionCodeSession();
        StopLoopbackIfRunning();
        SetStreamState(StreamState.Capturing);
        SetFailureCode(null, clearWhenNull: true);
        StatusMessage = "ローカル接続を確認し、送信側の開始データを準備しています。";
        FlowStateLabel = "案内: 接続準備";
        VerificationCode = string.Empty;
        IsVerificationPending = false;
        CurrentSetupStep = SetupStep.PathDiagnosing;
        UpdateDiagnostics(new ConnectionDiagnostics(PathType: UsbTetheringDetector.ClassifyPrimaryPath()));

        var localOffer = await _bridge.CreateOfferAsync();
        UpdateDiagnosticsFromBridgeOr(localOffer.Diagnostics);
        if (!localOffer.Success)
        {
            SetFailureState(
                $"開始データの作成に失敗しました: {localOffer.ErrorMessage}",
                localOffer.Diagnostics.NormalizedFailureCode ?? FailureCode.WebRtcNegotiationFailed
            );
            return;
        }

        _localSenderFingerprint = localOffer.Fingerprint;
        ActiveSessionId = localOffer.SessionId;
        var expiresAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + PayloadTtlMs;
        var payload = PairingInitPayload.Create(
            sessionId: localOffer.SessionId,
            senderDeviceName: Environment.MachineName,
            senderPubKeyFingerprint: localOffer.Fingerprint,
            offerSdp: localOffer.OfferSdp,
            expiresAtUnixMs: expiresAtUnixMs
        );
        CurrentPayload = QrPayloadCodec.EncodeInit(payload);

        try
        {
            StartConnectionCodeSession(CurrentPayload, localOffer.OfferSdp, expiresAtUnixMs);
        }
        catch (SessionFailure failure)
        {
            SetFailureState(failure.Message, failure.Code);
            return;
        }
        catch (Exception ex)
        {
            SetFailureState($"接続コードの作成に失敗しました: {ex.Message}", FailureCode.NetworkInterfaceNotUsable);
            return;
        }

        CurrentSetupStep = SetupStep.SenderShowInit;
        StatusMessage = "接続コードを作成しました。コピーして Android 側に貼り付けてください。";
        FlowStateLabel = "案内: 開始データを共有";
    }

    public void StartListener()
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        ResetConnectionCodeSession();
        _bridge.Close();
        StopLoopbackIfRunning();
        _playbackService.Stop();
        _pendingAnswerSdp = string.Empty;
        _localSenderFingerprint = string.Empty;
        CurrentPayload = string.Empty;
        CurrentConnectionCode = string.Empty;
        VerificationCode = string.Empty;
        ActiveSessionId = string.Empty;
        IsVerificationPending = false;
        CurrentSetupStep = SetupStep.PathDiagnosing;
        FlowStateLabel = "案内: 接続準備";
        StatusMessage = "ローカル接続を確認しています。";
        SetStreamState(StreamState.Idle);
        SetFailureCode(null, clearWhenNull: true);
        UpdateDiagnostics(new ConnectionDiagnostics(PathType: UsbTetheringDetector.ClassifyPrimaryPath()));
        RefreshDiagnosticsFromBridge();
        CurrentSetupStep = SetupStep.ListenerScanInit;
        FlowStateLabel = "案内: 開始データを入力";
        StatusMessage = "送信側から受け取った開始データを貼り付けてください。";
    }

    public async Task ProcessInputPayloadAsync(string rawPayload)
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            StatusMessage = "接続データが空です。";
            return;
        }

        if (CurrentSetupStep is SetupStep.SenderShowInit or SetupStep.SenderVerifyCode)
        {
            await PrepareConfirmForVerificationAsync(rawPayload);
            return;
        }

        if (CurrentSetupStep == SetupStep.ListenerShowConfirm)
        {
            StatusMessage = "応答データはすでに作成済みです。相手に共有するか、停止してやり直してください。";
            return;
        }

        CurrentSetupStep = SetupStep.ListenerScanInit;
        FlowStateLabel = "案内: 開始データを入力";
        await CreateConfirmFromInitAsync(rawPayload);
    }

    public async Task PasteFromClipboardAsync()
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        var dataPackage = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        if (!dataPackage.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            StatusMessage = "クリップボードに文字データがありません。";
            return;
        }
        var text = await dataPackage.GetTextAsync();
        await ProcessInputPayloadAsync(text);
    }

    public async Task ApproveVerificationAndConnectAsync()
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_pendingAnswerSdp))
        {
            StatusMessage = "適用できる応答データがありません。";
            return;
        }

        SetStreamState(StreamState.Connecting);
        var applyResult = await _bridge.ApplyAnswerAsync(_pendingAnswerSdp);
        UpdateDiagnosticsFromBridgeOr(applyResult.Diagnostics);
        if (!applyResult.Success)
        {
            SetFailureState(
                $"応答データの適用に失敗しました: {applyResult.ErrorMessage}",
                applyResult.Diagnostics.NormalizedFailureCode ?? FailureCode.WebRtcNegotiationFailed
            );
            return;
        }

        IsVerificationPending = false;
        FlowStateLabel = "案内: 接続中";
        SetStreamState(StreamState.Streaming);
        StatusMessage = "接続しました。このPCの音声送信を開始します。";
        RefreshDiagnosticsFromBridge();

        try
        {
            _loopbackSender.Start();
        }
        catch (Exception ex)
        {
            SetFailureState($"音声取得に失敗しました: {ex.Message}", FailureCode.AudioCaptureNotSupported);
        }
    }

    public void RejectVerificationAndRestart()
    {
        RestartSetup("6桁コードが一致しませんでした。", FailureCode.InvalidPayload);
    }

    public void Stop()
    {
        ResetConnectionCodeSession();
        _bridge.Close();
        StopLoopbackIfRunning();
        _playbackService.Stop();

        _pendingAnswerSdp = string.Empty;
        _localSenderFingerprint = string.Empty;
        CurrentPayload = string.Empty;
        CurrentConnectionCode = string.Empty;
        VerificationCode = string.Empty;
        ActiveSessionId = string.Empty;
        IsVerificationPending = false;
        CurrentSetupStep = SetupStep.Entry;
        FlowStateLabel = "案内: 最初の選択";
        StatusMessage = "接続を終了しました。";
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

    public async Task InitializeAsync()
    {
        if (_isStartupComplete)
        {
            return;
        }

        if (_startupTask is null)
        {
            if (_startupBridgeFactory is null)
            {
                _isStartupComplete = true;
                return;
            }

            _startupTask = InitializeDeferredAsync();
        }

        await _startupTask;
    }

    private async Task InitializeDeferredAsync()
    {
        var startupState = await Task.Run(() => CreateStartupState(_startupBridgeFactory!));

        RunOnUiThread(() =>
        {
            if (_receiveLoopCts.IsCancellationRequested)
            {
                startupState.Bridge.Close();
                return;
            }

            ApplyStartupState(startupState);
        });
    }

    private void ApplyStartupState(StartupState startupState)
    {
        _bridge = startupState.Bridge;
        _backendHealth = startupState.BackendHealth;

        BackendLabel = _bridge.IsNativeBackend
            ? "内部処理: ネイティブ接続モジュール"
            : _backendHealth.IsDevelopmentStub
                ? "内部処理: 開発用スタブ"
                : "内部処理: ネイティブ接続モジュール未検出";

        UpdateDiagnostics(new ConnectionDiagnostics(PathType: UsbTetheringDetector.ClassifyPrimaryPath()));

        if (_backendHealth.IsReady)
        {
            SetFailureCode(null, clearWhenNull: true);
            StatusMessage = _backendHealth.Message;
            SetStreamState(StreamState.Idle);
        }
        else
        {
            SetStreamState(StreamState.Idle);
            SetFailureCode(_backendHealth.BlockingFailureCode ?? FailureCode.WebRtcNegotiationFailed);
            StatusMessage = $"接続モジュールが利用できません。{_backendHealth.Message}";
        }

        EnsureReceiveLoopStarted();
        _isStartupComplete = true;
        NotifyComputedProperties();
    }

    private void EnsureReceiveLoopStarted()
    {
        if (_receiveLoopStarted)
        {
            return;
        }

        _receiveLoopStarted = true;
        _ = Task.Run(() => ReceiveLoopAsync(_receiveLoopCts.Token));
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
            UpdateDiagnosticsFromBridgeOr(answer.Diagnostics);
            if (!answer.Success)
            {
                SetFailureState(
                    $"応答データの作成に失敗しました: {answer.ErrorMessage}",
                    answer.Diagnostics.NormalizedFailureCode ?? FailureCode.WebRtcNegotiationFailed
                );
                return;
            }

            var confirmPayload = PairingConfirmPayload.Create(
                sessionId: initPayload.SessionId,
                receiverDeviceName: Environment.MachineName,
                receiverPubKeyFingerprint: answer.Fingerprint,
                answerSdp: answer.AnswerSdp,
                expiresAtUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + PayloadTtlMs
            );
            CurrentPayload = QrPayloadCodec.EncodeConfirm(confirmPayload);
            VerificationCode = P2PAudio.Windows.Core.Protocol.VerificationCode.FromSessionAndFingerprints(
                sessionId: initPayload.SessionId,
                senderFingerprint: initPayload.SenderPubKeyFingerprint,
                receiverFingerprint: answer.Fingerprint
            );
            ActiveSessionId = initPayload.SessionId;
            CurrentSetupStep = SetupStep.ListenerShowConfirm;
            FlowStateLabel = "案内: 応答データを共有";
            StatusMessage = "応答データを作成しました。コピーして送信側へ共有し、接続を待ってください。";
            SetFailureCode(null, clearWhenNull: true);
            RefreshDiagnosticsFromBridge();
        }
        catch (SessionFailure failure)
        {
            RestartSetup(failure.Message, failure.Code);
        }
        catch (Exception ex)
        {
            RestartSetup($"開始データを処理できませんでした: {ex.Message}", FailureCode.InvalidPayload);
        }
    }

    private Task PrepareConfirmForVerificationAsync(string confirmPayloadRaw)
    {
        try
        {
            var preparedConfirm = DecodeConfirmPayload(confirmPayloadRaw);
            ResetConnectionCodeSession();
            VerificationCode = preparedConfirm.VerificationCode;
            _pendingAnswerSdp = preparedConfirm.Payload.AnswerSdp;
            CurrentSetupStep = SetupStep.SenderVerifyCode;
            IsVerificationPending = true;
            FlowStateLabel = "案内: 6桁コードを確認";
            SetStreamState(StreamState.Connecting);
            SetFailureCode(null, clearWhenNull: true);
            StatusMessage = "6桁コードを確認してから接続してください。";
        }
        catch (SessionFailure failure)
        {
            RestartSetup(failure.Message, failure.Code);
        }
        catch (Exception ex)
        {
            RestartSetup($"応答データを処理できませんでした: {ex.Message}", FailureCode.InvalidPayload);
        }
        return Task.CompletedTask;
    }

    private void StartConnectionCodeSession(string initPayload, string offerSdp, long expiresAtUnixMs)
    {
        var session = _connectionCodeSessionFactory.Create(initPayload, offerSdp, expiresAtUnixMs);
        _connectionCodeSession = session;
        CurrentConnectionCode = session.ConnectionCode;
        _connectionCodeWaitCts = new CancellationTokenSource();
        _ = AwaitConnectionCodeConfirmAsync(session, _connectionCodeWaitCts.Token);
        _ = MonitorConnectionCodeExpiryAsync(session.ExpiresAtUnixMs, _connectionCodeWaitCts.Token);
    }

    private async Task AwaitConnectionCodeConfirmAsync(
        IConnectionCodeSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            var confirmPayloadRaw = await session.WaitForConfirmPayloadAsync(cancellationToken);
            RunOnUiThread(() => DisposeConnectionCodeSession(clearCode: true));
            await ApplyConfirmFromConnectionCodeAsync(confirmPayloadRaw, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SessionFailure failure)
        {
            RunOnUiThread(() => RestartSetup(failure.Message, failure.Code));
        }
        catch (Exception ex)
        {
            RunOnUiThread(() => RestartSetup($"接続コードの受信に失敗しました: {ex.Message}", FailureCode.PeerUnreachable));
        }
        finally
        {
            RunOnUiThread(CancelConnectionCodeWait);
        }
    }

    private async Task MonitorConnectionCodeExpiryAsync(long expiresAtUnixMs, CancellationToken cancellationToken)
    {
        var remainingMs = Math.Max(0, expiresAtUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(remainingMs), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (CurrentSetupStep == SetupStep.SenderShowInit && !string.IsNullOrWhiteSpace(CurrentConnectionCode))
            {
                RestartSetup("接続コードの有効期限が切れました。", FailureCode.SessionExpired);
            }
        });
    }

    private async Task ApplyConfirmFromConnectionCodeAsync(
        string confirmPayloadRaw,
        CancellationToken cancellationToken)
    {
        var preparedConfirm = DecodeConfirmPayload(confirmPayloadRaw);

        RunOnUiThread(() =>
        {
            VerificationCode = preparedConfirm.VerificationCode;
            _pendingAnswerSdp = preparedConfirm.Payload.AnswerSdp;
            IsVerificationPending = false;
            FlowStateLabel = "案内: 接続中";
            SetStreamState(StreamState.Connecting);
            SetFailureCode(null, clearWhenNull: true);
            StatusMessage = "Android から応答データを受信しました。接続しています。";
        });

        var applyResult = await _bridge.ApplyAnswerAsync(preparedConfirm.Payload.AnswerSdp);
        cancellationToken.ThrowIfCancellationRequested();

        RunOnUiThread(() =>
        {
            UpdateDiagnosticsFromBridgeOr(applyResult.Diagnostics);
            if (!applyResult.Success)
            {
                SetFailureState(
                    $"応答データの適用に失敗しました: {applyResult.ErrorMessage}",
                    applyResult.Diagnostics.NormalizedFailureCode ?? FailureCode.WebRtcNegotiationFailed
                );
                return;
            }

            IsVerificationPending = false;
            FlowStateLabel = "案内: 接続中";
            SetStreamState(StreamState.Streaming);
            StatusMessage = "接続しました。このPCの音声送信を開始します。";
            RefreshDiagnosticsFromBridge();

            try
            {
                _loopbackSender.Start();
            }
            catch (Exception ex)
            {
                SetFailureState($"音声取得に失敗しました: {ex.Message}", FailureCode.AudioCaptureNotSupported);
            }
        });
    }

    private PreparedConfirm DecodeConfirmPayload(string confirmPayloadRaw)
    {
        var confirmPayload = QrPayloadCodec.DecodeConfirm(confirmPayloadRaw);
        var failure = PairingPayloadValidator.ValidateConfirm(
            confirmPayload,
            expectedSessionId: ActiveSessionId,
            nowUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        if (failure is not null)
        {
            throw failure;
        }

        if (string.IsNullOrWhiteSpace(_localSenderFingerprint))
        {
            throw new SessionFailure(FailureCode.InvalidPayload, "送信側の情報が不足しています。");
        }

        return new PreparedConfirm(
            Payload: confirmPayload,
            VerificationCode: P2PAudio.Windows.Core.Protocol.VerificationCode.FromSessionAndFingerprints(
                sessionId: ActiveSessionId,
                senderFingerprint: _localSenderFingerprint,
                receiverFingerprint: confirmPayload.ReceiverPubKeyFingerprint
            )
        );
    }

    private void ResetConnectionCodeSession()
    {
        CancelConnectionCodeWait();
        DisposeConnectionCodeSession(clearCode: true);
    }

    private void CancelConnectionCodeWait()
    {
        _connectionCodeWaitCts?.Cancel();
        _connectionCodeWaitCts?.Dispose();
        _connectionCodeWaitCts = null;
    }

    private void DisposeConnectionCodeSession(bool clearCode)
    {
        _connectionCodeSession?.Dispose();
        _connectionCodeSession = null;

        if (clearCode)
        {
            CurrentConnectionCode = string.Empty;
        }
    }

    private bool EnsureBackendReady()
    {
        if (_backendHealth.IsReady)
        {
            return true;
        }

        SetFailureState(
            $"接続モジュールが利用できません。{_backendHealth.Message}",
            _backendHealth.BlockingFailureCode ?? FailureCode.WebRtcNegotiationFailed
        );
        return false;
    }

    private void RestartSetup(string message, FailureCode code)
    {
        ResetConnectionCodeSession();
        _bridge.Close();
        StopLoopbackIfRunning();
        _playbackService.Stop();
        _pendingAnswerSdp = string.Empty;
        _localSenderFingerprint = string.Empty;
        CurrentPayload = string.Empty;
        CurrentConnectionCode = string.Empty;
        VerificationCode = string.Empty;
        ActiveSessionId = string.Empty;
        IsVerificationPending = false;
        CurrentSetupStep = SetupStep.Entry;
        FlowStateLabel = "案内: 最初からやり直し";
        SetStreamState(StreamState.Idle);
        SetFailureCode(code);
        StatusMessage = $"{message} 最初からやり直してください。";
    }

    private void SetFailureState(string message, FailureCode code)
    {
        ResetConnectionCodeSession();
        CurrentSetupStep = SetupStep.Entry;
        FlowStateLabel = "案内: 最初からやり直し";
        IsVerificationPending = false;
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
            FailureCodeLabel = $"原因コード: {FailureCodeMapper.ToWireValue(code.Value)}";
            return;
        }

        if (clearWhenNull)
        {
            FailureCodeLabel = "原因コード: -";
        }
    }

    private void SetStreamState(StreamState state)
    {
        CurrentStreamState = state;
        StreamStateLabel = DescribeStreamState(state);
        NotifyComputedProperties();
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
                    if (!_playbackService.PlayPacket(packet))
                    {
                        continue;
                    }

                    RunOnUiThread(() =>
                    {
                        if (CurrentStreamState == StreamState.Connecting && CurrentSetupStep == SetupStep.ListenerShowConfirm)
                        {
                            SetStreamState(StreamState.Streaming);
                            FlowStateLabel = "案内: 接続済み";
                            StatusMessage = "相手の音声を受信しています。";
                        }
                        else if (CurrentStreamState == StreamState.Interrupted)
                        {
                            SetStreamState(StreamState.Streaming);
                            StatusMessage = "接続が復旧しました。";
                        }
                    });
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
            RunOnUiThread(() => SetFailureState($"受信処理で問題が発生しました: {ex.Message}", FailureCode.WebRtcNegotiationFailed));
        }
    }

    private void EvaluateStreamHealth()
    {
        try
        {
            var diagnostics = _bridge.GetDiagnostics();
            var code = diagnostics.NormalizedFailureCode ?? FailureCodeMapper.FromFailureHint(diagnostics.FailureHint);
            RunOnUiThread(() =>
            {
                UpdateDiagnostics(diagnostics);

                if (code is not null)
                {
                    if (code == FailureCode.NetworkChanged || code == FailureCode.PeerUnreachable)
                    {
                        SetInterruptedState($"接続が中断しました: {LocalizeFailureHint(diagnostics.FailureHint)}", code.Value);
                    }
                    else if (code == FailureCode.WebRtcNegotiationFailed && CurrentStreamState == StreamState.Streaming)
                    {
                        SetInterruptedState($"通信が不安定です: {LocalizeFailureHint(diagnostics.FailureHint)}", code.Value);
                    }
                }
            });
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
            if (HasMeaningfulDiagnostics(diagnostics))
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

    private void UpdateDiagnosticsFromBridgeOr(ConnectionDiagnostics fallbackDiagnostics)
    {
        try
        {
            var bridgeDiagnostics = _bridge.GetDiagnostics();
            if (HasMeaningfulDiagnostics(bridgeDiagnostics))
            {
                UpdateDiagnostics(bridgeDiagnostics);
                return;
            }
        }
        catch
        {
        }

        UpdateDiagnostics(fallbackDiagnostics);
    }

    private void UpdateDiagnostics(ConnectionDiagnostics diagnostics)
    {
        NetworkPathLabel = diagnostics.PathType switch
        {
            NetworkPathType.WifiLan => "接続経路: Wi-Fi / ローカルネットワーク",
            NetworkPathType.UsbTether => "接続経路: USBテザリング",
            _ => "接続経路: 判定前"
        };
        CandidateCountLabel = $"利用可能なローカル候補: {diagnostics.LocalCandidatesCount}";
        SelectedCandidatePairLabel = string.IsNullOrWhiteSpace(diagnostics.SelectedCandidatePairType)
            ? "選択中の接続経路: -"
            : $"選択中の接続経路: {diagnostics.SelectedCandidatePairType}";
        FailureHintLabel = string.IsNullOrWhiteSpace(diagnostics.FailureHint)
            ? string.Empty
            : $"補足: {LocalizeFailureHint(diagnostics.FailureHint)}";

        var mappedCode = diagnostics.NormalizedFailureCode ?? FailureCodeMapper.FromFailureHint(diagnostics.FailureHint);
        if (mappedCode is not null)
        {
            SetFailureCode(mappedCode.Value);
        }
    }

    private static bool HasMeaningfulDiagnostics(ConnectionDiagnostics diagnostics)
    {
        return diagnostics.PathType != NetworkPathType.Unknown ||
               diagnostics.LocalCandidatesCount > 0 ||
               !string.IsNullOrWhiteSpace(diagnostics.SelectedCandidatePairType) ||
               !string.IsNullOrWhiteSpace(diagnostics.FailureHint) ||
               diagnostics.NormalizedFailureCode is not null;
    }

    private void RunOnUiThread(Action action)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            action();
            return;
        }

        Exception? failure = null;
        using var completed = new ManualResetEventSlim(false);
        _uiContext.Post(_ =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.Set();
            }
        }, null);
        completed.Wait();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
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
                startupReason: FormatBridgeStartupReason(ex)
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

    private static IWebRtcBridge CreateStartupPlaceholderBridge()
    {
        return new StubWebRtcBridge(
            enabledForDevelopment: false,
            startupReason: "接続モジュールの初期化がまだ完了していません。"
        );
    }

    private static BridgeBackendHealth CreatePendingBackendHealth()
    {
        return new BridgeBackendHealth(
            IsReady: false,
            IsDevelopmentStub: false,
            Message: "ネイティブ接続モジュールを起動しています。",
            BlockingFailureCode: null
        );
    }

    private static StartupState CreateStartupState(Func<IWebRtcBridge> bridgeFactory)
    {
        try
        {
            return CreateStartupState(bridgeFactory());
        }
        catch (Exception ex)
        {
            var fallbackBridge = new StubWebRtcBridge(
                enabledForDevelopment: false,
                startupReason: FormatBridgeStartupReason(ex)
            );
            return new StartupState(fallbackBridge, fallbackBridge.GetBackendHealth());
        }
    }

    private static StartupState CreateStartupState(IWebRtcBridge bridge)
    {
        try
        {
            return new StartupState(bridge, bridge.GetBackendHealth());
        }
        catch (Exception ex)
        {
            var fallbackBridge = new StubWebRtcBridge(
                enabledForDevelopment: false,
                startupReason: FormatBridgeStartupReason(ex)
            );
            return new StartupState(fallbackBridge, fallbackBridge.GetBackendHealth());
        }
    }

    private static string FormatBridgeStartupReason(Exception ex)
    {
        return NativeWebRtcLibraryResolver.IsNativeLoadFailure(ex)
            ? NativeWebRtcLibraryResolver.DescribeStartupFailure(ex)
            : ex.Message;
    }

    partial void OnCurrentSetupStepChanged(SetupStep value)
    {
        NotifyComputedProperties();
    }

    partial void OnVerificationCodeChanged(string value)
    {
        OnPropertyChanged(nameof(VerificationCodeDisplay));
    }

    partial void OnIsVerificationPendingChanged(bool value)
    {
        NotifyComputedProperties();
    }

    private void NotifyComputedProperties()
    {
        OnPropertyChanged(nameof(RecommendedAction));
        OnPropertyChanged(nameof(CanStartSender));
        OnPropertyChanged(nameof(CanStartListener));
        OnPropertyChanged(nameof(CanProcessPayload));
        OnPropertyChanged(nameof(CanApproveCode));
        OnPropertyChanged(nameof(CanRejectCode));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(StreamStateIndicator));
        OnPropertyChanged(nameof(StepProgressLabel));
    }

    private string GetRecommendedAction()
    {
        if (!_backendHealth.IsReady && CurrentSetupStep == SetupStep.Entry)
            return "ネイティブ接続モジュールが見つかりません。必要なランタイムを入れてから再起動してください。";
        if (CurrentStreamState == StreamState.Streaming)
            return "音声共有中です。終了するときは「接続を終了する」を押してください。";
        if (CurrentStreamState == StreamState.Failed)
        {
            return NetworkPathLabel.Contains("USB") && CandidateCountLabel.Contains(": 0")
                ? "USBテザリングを有効にして、もう一度やり直してください。"
                : CandidateCountLabel.Contains(": 0")
                    ? "Wi-FiまたはUSBテザリングの接続状態を確認してからやり直してください。"
                    : "接続を終了してから、もう一度やり直してください。";
        }
        if (CurrentStreamState == StreamState.Interrupted)
            return "接続の復旧を待っています。両端末のネットワークを確認してください。";
        return CurrentSetupStep switch
        {
            SetupStep.Entry => "送信側か受信側を選ぶと、画面の案内に沿って進められます。",
            SetupStep.PathDiagnosing => "同じネットワーク上で直接つなぐための準備をしています。",
            SetupStep.SenderShowInit => "接続コードをコピーして Android 側に貼り付けてください。従来どおり開始データの手動共有も使えます。",
            SetupStep.SenderVerifyCode => "両端末の6桁コードが同じなら接続してください。",
            SetupStep.ListenerScanInit => "送信側から受け取った開始データを貼り付けてください。",
            SetupStep.ListenerShowConfirm => "応答データと6桁コードを送信側へ共有して接続を待ってください。",
            _ => string.Empty
        };
    }

    private static string DescribeStreamState(StreamState state)
    {
        return state switch
        {
            StreamState.Idle => "待機中",
            StreamState.Capturing => "準備中",
            StreamState.Connecting => "接続中",
            StreamState.Streaming => "接続済み",
            StreamState.Interrupted => "一時中断",
            StreamState.Failed => "対応が必要",
            StreamState.Ended => "停止済み",
            _ => "待機中"
        };
    }

    private static string FormatVerificationCode(string value)
    {
        if (value.Length != 6)
        {
            return value;
        }

        return $"{value[..3]} - {value[3..]}";
    }

    private static string LocalizeFailureHint(string failureHint)
    {
        return failureHint switch
        {
            "usb_tether_detected_but_not_reachable" => "USB接続は見つかりましたが通信できません。",
            "usb_tether_unavailable" => "USBテザリングが利用できません。",
            "usb_tether_check" => "USBケーブル、信頼ダイアログ、USBテザリング設定を確認してください。",
            "wifi_lan_check" => "両端末が同じWi-Fiまたは同じローカルネットワーク上にあるか確認してください。",
            "network_interface_check" => "利用可能なローカルネットワークが見つかりませんでした。",
            "peer_disconnected" => "相手端末との接続が切れました。",
            "network_changed" => "ネットワークが変更されました。",
            "native_backend_unavailable" => "接続モジュールが利用できません。",
            _ => failureHint
        };
    }

    private sealed record PreparedConfirm(PairingConfirmPayload Payload, string VerificationCode);

    private sealed record StartupState(IWebRtcBridge Bridge, BridgeBackendHealth BackendHealth);
}

public enum SetupStep
{
    Entry,
    PathDiagnosing,
    ListenerScanInit,
    SenderShowInit,
    SenderVerifyCode,
    ListenerShowConfirm
}
