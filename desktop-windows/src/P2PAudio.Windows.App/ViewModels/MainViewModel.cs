using System.Runtime.ExceptionServices;
using CommunityToolkit.Mvvm.ComponentModel;
using P2PAudio.Windows.App.Logging;
using P2PAudio.Windows.App.Services;
using P2PAudio.Windows.Core.Models;
using P2PAudio.Windows.Core.Networking;
using P2PAudio.Windows.Core.Protocol;

namespace P2PAudio.Windows.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const long PayloadTtlMs = 600_000;
    private const int ReceiveLoopIdleDelayMs = 5;
    private const int ReceiveHealthCheckIdleTicks = 100;
    private const long ReceiveStatsLogIntervalMs = 5_000;

    private IWebRtcBridge _bridge;
    private readonly Func<IWebRtcBridge>? _startupBridgeFactory;
    private readonly IUdpAudioSenderBridge _udpBridge;
    private readonly IUdpReceiverDiscoveryService _udpReceiverDiscoveryService;
    private readonly IConnectionCodeSessionFactory _connectionCodeSessionFactory;
    private readonly Func<ILoopbackAudioSender> _webRtcLoopbackSenderFactory;
    private readonly Func<ILoopbackAudioSender> _udpLoopbackSenderFactory;
    private readonly PcmPlaybackService _playbackService;
    private readonly CancellationTokenSource _receiveLoopCts = new();
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private BridgeBackendHealth _backendHealth;
    private BridgeBackendHealth _udpBackendHealth;
    private Task? _startupTask;
    private bool _isStartupComplete;
    private bool _receiveLoopStarted;

    private string _localSenderFingerprint = string.Empty;
    private string _pendingAnswerSdp = string.Empty;
    private int _receiveIdleTicks;
    private long _receivedPackets;
    private long _lastReceiveStatsLogAtMs = Environment.TickCount64;
    private IConnectionCodeSession? _connectionCodeSession;
    private CancellationTokenSource? _connectionCodeWaitCts;
    private ILoopbackAudioSender? _webRtcLoopbackSender;
    private ILoopbackAudioSender? _udpLoopbackSender;

    public MainViewModel() : this(initializeImmediately: true)
    {
    }

    public MainViewModel(bool initializeImmediately)
        : this(
            initialBridge: initializeImmediately ? CreateBridge() : CreateStartupPlaceholderBridge(),
            startupBridgeFactory: initializeImmediately ? null : CreateBridge,
            udpBridge: CreateUdpBridge(),
            udpReceiverDiscoveryService: new MdnsUdpReceiverDiscoveryService(),
            connectionCodeSessionFactory: null,
            initializeImmediately: initializeImmediately
        )
    {
    }

    public MainViewModel(IWebRtcBridge bridge) : this(
        bridge,
        udpBridge: new StubUdpAudioSenderBridge("UDP + Opus 送信モジュールはこのコンストラクターでは指定されていません。"),
        udpReceiverDiscoveryService: new MdnsUdpReceiverDiscoveryService(),
        connectionCodeSessionFactory: null,
        initializeImmediately: true
    )
    {
    }

    public MainViewModel(IWebRtcBridge bridge, bool initializeImmediately)
        : this(
            bridge,
            udpBridge: new StubUdpAudioSenderBridge("UDP + Opus 送信モジュールはこのコンストラクターでは指定されていません。"),
            udpReceiverDiscoveryService: new MdnsUdpReceiverDiscoveryService(),
            connectionCodeSessionFactory: null,
            initializeImmediately: initializeImmediately
        )
    {
    }

    public MainViewModel(
        IWebRtcBridge bridge,
        IUdpAudioSenderBridge udpBridge,
        IUdpReceiverDiscoveryService udpReceiverDiscoveryService,
        IConnectionCodeSessionFactory? connectionCodeSessionFactory,
        bool initializeImmediately,
        Func<ILoopbackAudioSender>? webRtcLoopbackSenderFactory = null,
        Func<ILoopbackAudioSender>? udpLoopbackSenderFactory = null)
        : this(
            initialBridge: initializeImmediately ? bridge : CreateStartupPlaceholderBridge(),
            startupBridgeFactory: initializeImmediately ? null : () => bridge,
            udpBridge: udpBridge,
            udpReceiverDiscoveryService: udpReceiverDiscoveryService,
            connectionCodeSessionFactory: connectionCodeSessionFactory,
            initializeImmediately: initializeImmediately,
            webRtcLoopbackSenderFactory: webRtcLoopbackSenderFactory,
            udpLoopbackSenderFactory: udpLoopbackSenderFactory
        )
    {
    }

    public MainViewModel(
        IWebRtcBridge bridge,
        IConnectionCodeSessionFactory? connectionCodeSessionFactory,
        bool initializeImmediately)
        : this(
            bridge,
            udpBridge: new StubUdpAudioSenderBridge("UDP + Opus 送信モジュールはこのコンストラクターでは指定されていません。"),
            udpReceiverDiscoveryService: new MdnsUdpReceiverDiscoveryService(),
            connectionCodeSessionFactory: connectionCodeSessionFactory,
            initializeImmediately: initializeImmediately
        )
    {
    }

    private MainViewModel(
        IWebRtcBridge initialBridge,
        Func<IWebRtcBridge>? startupBridgeFactory,
        IUdpAudioSenderBridge udpBridge,
        IUdpReceiverDiscoveryService udpReceiverDiscoveryService,
        IConnectionCodeSessionFactory? connectionCodeSessionFactory,
        bool initializeImmediately,
        Func<ILoopbackAudioSender>? webRtcLoopbackSenderFactory = null,
        Func<ILoopbackAudioSender>? udpLoopbackSenderFactory = null)
    {
        _bridge = initialBridge;
        _startupBridgeFactory = startupBridgeFactory;
        _udpBridge = udpBridge;
        _udpReceiverDiscoveryService = udpReceiverDiscoveryService;
        _connectionCodeSessionFactory = connectionCodeSessionFactory ?? new ConnectionCodeSessionFactory();
        _webRtcLoopbackSenderFactory = webRtcLoopbackSenderFactory
            ?? (() => new LoopbackPcmSender(packet => _bridge.SendPcmPacket(packet)));
        _udpLoopbackSenderFactory = udpLoopbackSenderFactory
            ?? (() => new LoopbackPcmSender(
                frame => _udpBridge.SendPcmFrame(frame),
                new LoopbackCaptureOptions(targetSampleRate: 48_000)));
        _playbackService = new PcmPlaybackService();
        _backendHealth = CreatePendingBackendHealth();
        _udpBackendHealth = CreateBackendHealth(_udpBridge);

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

    [ObservableProperty]
    private TransportMode selectedTransportMode = TransportMode.WebRtc;

    public string RecommendedAction => GetRecommendedAction();

    public int SelectedTransportModeIndex => SelectedTransportMode == TransportMode.WebRtc ? 0 : 1;

    public string TransportModeLabel => SelectedTransportMode switch
    {
        TransportMode.WebRtc => "転送モード: WebRTC",
        TransportMode.UdpOpus => "転送モード: UDP + Opus",
        _ => "転送モード: WebRTC"
    };

    public string TransportModeDescription => SelectedTransportMode switch
    {
        TransportMode.WebRtc => "既存の WebRTC 接続と接続コードの流れを使います。",
        TransportMode.UdpOpus => "WebRTC と同じ接続コードの流れで、この PC のメディア音声を Opus + UDP で Android へ低遅延送信します。",
        _ => string.Empty
    };

    public string SenderEntryDescription => SelectedTransportMode switch
    {
        TransportMode.WebRtc => "このPCの音を相手へ",
        TransportMode.UdpOpus => "このPCで再生中の音を Android へ低遅延送信",
        _ => string.Empty
    };

    public string ListenerEntryDescription => SelectedTransportMode switch
    {
        TransportMode.WebRtc => "相手の音をこのPCで再生",
        TransportMode.UdpOpus => "UDP + Opus では Windows 側の受信は未対応です",
        _ => string.Empty
    };

    public string PathStepTitle => CurrentSetupStep switch
    {
        SetupStep.UdpSenderDiscovering => "Android の受信待機を探しています…",
        _ => "接続の準備をしています…"
    };

    public string PathStepDescription => CurrentSetupStep switch
    {
        SetupStep.UdpSenderDiscovering => "同じ LAN 上で mDNS を使って Android の UDP + Opus 受信待機を検索しています。",
        _ => "同じネットワーク上で直接つなぐための確認をしています。"
    };

    public double ProgressValue => SelectedTransportMode == TransportMode.UdpOpus &&
        CurrentStreamState is StreamState.Streaming or StreamState.Interrupted
            ? 3
            : CurrentSetupStep switch
            {
                SetupStep.Entry => 1,
                SetupStep.PathDiagnosing => 2,
                SetupStep.UdpSenderDiscovering => 2,
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

    public string StepProgressLabel => SelectedTransportMode == TransportMode.UdpOpus &&
        CurrentStreamState is StreamState.Streaming or StreamState.Interrupted
            ? "\u2462 UDP + Opus \u9001\u4FE1\u4E2D"
            : CurrentSetupStep switch
            {
                SetupStep.Entry => "\u2460 \u5F79\u5272\u3092\u9078\u3076",
                SetupStep.PathDiagnosing => "\u2460 \u6E96\u5099\u4E2D",
                SetupStep.UdpSenderDiscovering => "\u2461 Android \u53D7\u4FE1\u6A5F\u3092\u63A2\u3059",
                SetupStep.SenderShowInit => "\u2461 \u30C7\u30FC\u30BF\u3092\u5171\u6709",
                SetupStep.ListenerScanInit => "\u2461 \u30C7\u30FC\u30BF\u3092\u5165\u529B",
                SetupStep.SenderVerifyCode => "\u2462 \u30B3\u30FC\u30C9\u78BA\u8A8D",
                SetupStep.ListenerShowConfirm => "\u2462 \u30B3\u30FC\u30C9\u78BA\u8A8D",
                _ => string.Empty
            };

    public bool CanStartSender => GetSelectedBackendHealth().IsReady &&
        (CurrentSetupStep == SetupStep.Entry || CurrentStreamState == StreamState.Failed) &&
        CurrentStreamState is StreamState.Idle or StreamState.Ended or StreamState.Failed;

    public bool CanStartListener => SelectedTransportMode == TransportMode.WebRtc &&
        _backendHealth.IsReady &&
        (CurrentSetupStep == SetupStep.Entry || CurrentStreamState == StreamState.Failed) &&
        CurrentStreamState is StreamState.Idle or StreamState.Ended or StreamState.Failed;

    public bool CanProcessPayload => SelectedTransportMode == TransportMode.WebRtc &&
        _backendHealth.IsReady &&
        CurrentSetupStep is SetupStep.ListenerScanInit or SetupStep.SenderShowInit or SetupStep.SenderVerifyCode;

    public bool CanApproveCode => SelectedTransportMode == TransportMode.WebRtc &&
        _backendHealth.IsReady && IsVerificationPending;

    public bool CanRejectCode => IsVerificationPending;

    public bool CanStop => CurrentSetupStep != SetupStep.Entry || CurrentStreamState != StreamState.Idle;

    public void SelectTransportMode(TransportMode mode)
    {
        if (SelectedTransportMode == mode)
        {
            return;
        }

        if (CurrentSetupStep != SetupStep.Entry ||
            CurrentStreamState is StreamState.Capturing or StreamState.Connecting or StreamState.Streaming or StreamState.Interrupted)
        {
            return;
        }

        ResetConnectionCodeSession();
        _bridge.Close();
        _udpBridge.StopStreaming();
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
        SetStreamState(StreamState.Idle);
        SetFailureCode(null, clearWhenNull: true);
        UpdateDiagnostics(new ConnectionDiagnostics(PathType: UsbTetheringDetector.ClassifyPrimaryPath()));

        SelectedTransportMode = mode;
        StatusMessage = mode switch
        {
            TransportMode.WebRtc => "WebRTC モードです。接続コードまたは手動の接続データ共有で進めます。",
            TransportMode.UdpOpus => "UDP + Opus モードです。接続コードを Android 側に貼り付けると、Windows のメディア音声送信を開始できます。",
            _ => "準備できました。"
        };
    }

    public async Task StartSenderAsync()
    {
        if (SelectedTransportMode == TransportMode.UdpOpus)
        {
            await StartUdpSenderAsync();
            return;
        }

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
        if (SelectedTransportMode == TransportMode.UdpOpus)
        {
            StatusMessage = "UDP + Opus では Windows 側の受信は未対応です。WebRTC モードに切り替えてください。";
            SetStreamState(StreamState.Idle);
            SetFailureCode(null, clearWhenNull: true);
            CurrentSetupStep = SetupStep.Entry;
            FlowStateLabel = "案内: 最初の選択";
            return;
        }

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

    private async Task StartUdpSenderAsync()
    {
        if (!EnsureBackendReady())
        {
            return;
        }

        ResetConnectionCodeSession();
        _bridge.Close();
        _udpBridge.StopStreaming();
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
        StatusMessage = "UDP + Opus 用の接続コードを準備しています。";
        SetFailureCode(null, clearWhenNull: true);
        SetStreamState(StreamState.Connecting);
        UpdateDiagnostics(new ConnectionDiagnostics(
            PathType: UsbTetheringDetector.ClassifyPrimaryPath(),
            SelectedCandidatePairType: "udp_opus"
        ));

        try
        {
            var expiresAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + PayloadTtlMs;
            var sessionId = Guid.NewGuid().ToString();
            var payload = UdpInitPayload.Create(
                sessionId: sessionId,
                senderDeviceName: Environment.MachineName,
                expiresAtUnixMs: expiresAtUnixMs
            );

            ActiveSessionId = sessionId;
            CurrentPayload = QrPayloadCodec.EncodeUdpInit(payload);
            StartConnectionCodeSession(CurrentPayload, localAddressHintSource: string.Empty, expiresAtUnixMs);
        }
        catch (SessionFailure failure)
        {
            SetFailureState(failure.Message, failure.Code);
            return;
        }
        catch (Exception ex)
        {
            SetFailureState($"UDP + Opus の接続コード作成に失敗しました: {ex.Message}", FailureCode.NetworkInterfaceNotUsable);
            return;
        }

        CurrentSetupStep = SetupStep.SenderShowInit;
        FlowStateLabel = "案内: 接続コードを共有";
        StatusMessage = "UDP + Opus の接続コードを作成しました。コピーして Android 側に貼り付けてください。";
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
            if (SelectedTransportMode == TransportMode.UdpOpus)
            {
                StatusMessage = "UDP + Opus では接続コードの応答を自動処理します。Android 側で接続コードを貼り付けてください。";
                return;
            }

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
            EnsureWebRtcLoopbackSender().Start();
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
        _udpBridge.StopStreaming();
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
        UpdateDiagnostics(new ConnectionDiagnostics(PathType: UsbTetheringDetector.ClassifyPrimaryPath()));
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
        UpdateBackendLabel();

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
        AppLogger.I("MainViewModel", "receive_loop_started", "Receive loop started");
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

    private void StartConnectionCodeSession(string initPayload, string localAddressHintSource, long expiresAtUnixMs)
    {
        var session = _connectionCodeSessionFactory.Create(initPayload, localAddressHintSource, expiresAtUnixMs);
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
            var confirmSubmission = await session.WaitForConfirmPayloadAsync(cancellationToken);
            RunOnUiThread(() => DisposeConnectionCodeSession(clearCode: true));
            if (SelectedTransportMode == TransportMode.UdpOpus)
            {
                await ApplyUdpConfirmFromConnectionCodeAsync(confirmSubmission, cancellationToken);
            }
            else
            {
                await ApplyConfirmFromConnectionCodeAsync(confirmSubmission.Payload, cancellationToken);
            }
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
                EnsureWebRtcLoopbackSender().Start();
            }
            catch (Exception ex)
            {
                SetFailureState($"音声取得に失敗しました: {ex.Message}", FailureCode.AudioCaptureNotSupported);
            }
        });
    }

    private async Task ApplyUdpConfirmFromConnectionCodeAsync(
        ConnectionCodeSubmission confirmSubmission,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(confirmSubmission.RemoteAddress))
        {
            throw new SessionFailure(FailureCode.PeerUnreachable, "Android の受信先アドレスを特定できませんでした。");
        }

        var confirmPayload = DecodeUdpConfirmPayload(confirmSubmission.Payload);

        RunOnUiThread(() =>
        {
            VerificationCode = string.Empty;
            IsVerificationPending = false;
            FlowStateLabel = "案内: 接続中";
            SetStreamState(StreamState.Connecting);
            SetFailureCode(null, clearWhenNull: true);
            StatusMessage = "Android から受信先情報を受信しました。UDP + Opus 接続を開始しています。";
        });

        var result = await _udpBridge.StartStreamingAsync(
            confirmSubmission.RemoteAddress,
            confirmPayload.ReceiverPort,
            confirmPayload.ReceiverDeviceName
        );
        cancellationToken.ThrowIfCancellationRequested();

        RunOnUiThread(() =>
        {
            var endpoint = new UdpReceiverEndpoint(
                DisplayName: confirmPayload.ReceiverDeviceName,
                ServiceName: confirmPayload.SessionId,
                Host: confirmSubmission.RemoteAddress,
                Port: confirmPayload.ReceiverPort
            );
            UpdateDiagnostics(ToUdpDiagnostics(result.Diagnostics, endpoint));
            if (!result.Success)
            {
                SetFailureState(
                    $"UDP + Opus の送信を開始できませんでした: {result.ErrorMessage}",
                    result.Diagnostics.NormalizedFailureCode ?? FailureCode.PeerUnreachable
                );
                return;
            }

            ActiveSessionId = confirmPayload.SessionId;
            FlowStateLabel = "案内: UDP + Opus 送信中";
            StatusMessage = result.StatusMessage;

            try
            {
                EnsureUdpLoopbackSender().Start();
            }
            catch (Exception ex)
            {
                _udpBridge.StopStreaming();
                SetFailureState($"音声取得に失敗しました: {ex.Message}", FailureCode.AudioCaptureNotSupported);
                return;
            }

            SetStreamState(StreamState.Streaming);
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

    private UdpConfirmPayload DecodeUdpConfirmPayload(string confirmPayloadRaw)
    {
        var confirmPayload = QrPayloadCodec.DecodeUdpConfirm(confirmPayloadRaw);
        var failure = PairingPayloadValidator.ValidateUdpConfirm(
            confirmPayload,
            expectedSessionId: ActiveSessionId,
            nowUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
        if (failure is not null)
        {
            throw failure;
        }

        return confirmPayload;
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
        var health = GetSelectedBackendHealth();
        if (health.IsReady)
        {
            return true;
        }

        SetFailureState(
            $"接続モジュールが利用できません。{health.Message}",
            health.BlockingFailureCode ?? FailureCode.WebRtcNegotiationFailed
        );
        return false;
    }

    private void RestartSetup(string message, FailureCode code)
    {
        AppLogger.W(
            "MainViewModel",
            "setup_restarted",
            "Restarting setup after a recoverable failure",
            new Dictionary<string, object?>
            {
                ["message"] = message,
                ["code"] = FailureCodeMapper.ToWireValue(code)
            }
        );
        ResetConnectionCodeSession();
        _bridge.Close();
        _udpBridge.StopStreaming();
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
        AppLogger.E(
            "MainViewModel",
            "stream_failed",
            "Stream entered failed state",
            new Dictionary<string, object?>
            {
                ["message"] = message,
                ["code"] = FailureCodeMapper.ToWireValue(code)
            }
        );
        ResetConnectionCodeSession();
        _udpBridge.StopStreaming();
        StopLoopbackIfRunning();
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

        AppLogger.W(
            "MainViewModel",
            "stream_interrupted",
            "Stream entered interrupted state",
            new Dictionary<string, object?>
            {
                ["message"] = message,
                ["code"] = FailureCodeMapper.ToWireValue(code)
            }
        );
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
        StopLoopbackSender(_webRtcLoopbackSender);
        StopLoopbackSender(_udpLoopbackSender);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (SelectedTransportMode == TransportMode.UdpOpus)
                {
                    if (CurrentStreamState is StreamState.Connecting or StreamState.Streaming or StreamState.Interrupted)
                    {
                        EvaluateStreamHealth();
                    }
                    await Task.Delay(200, cancellationToken);
                    continue;
                }

                var receivedPacket = false;
                while (_bridge.TryReceivePcmPacket(out var packet))
                {
                    receivedPacket = true;
                    _receiveIdleTicks = 0;
                    _receivedPackets++;
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
                }

                if (receivedPacket)
                {
                    LogReceiveStatsIfNeeded();
                    continue;
                }

                _receiveIdleTicks++;
                if (_receiveIdleTicks % ReceiveHealthCheckIdleTicks == 0)
                {
                    EvaluateStreamHealth();
                }

                await Task.Delay(ReceiveLoopIdleDelayMs, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.E("MainViewModel", "receive_loop_failed", "Receive loop failed", exception: ex);
            RunOnUiThread(() => SetFailureState($"受信処理で問題が発生しました: {ex.Message}", FailureCode.WebRtcNegotiationFailed));
        }
    }

    private void LogReceiveStatsIfNeeded()
    {
        var now = Environment.TickCount64;
        if (now - _lastReceiveStatsLogAtMs < ReceiveStatsLogIntervalMs)
        {
            return;
        }

        _lastReceiveStatsLogAtMs = now;
        AppLogger.D(
            "MainViewModel",
            "receive_loop_stats",
            "Receive loop stats",
            new Dictionary<string, object?>
            {
                ["receivedPackets"] = _receivedPackets,
                ["streamState"] = CurrentStreamState.ToString(),
                ["setupStep"] = CurrentSetupStep.ToString(),
                ["idleTicks"] = _receiveIdleTicks
            }
        );
    }

    private void EvaluateStreamHealth()
    {
        try
        {
            var diagnostics = SelectedTransportMode == TransportMode.UdpOpus
                ? _udpBridge.GetDiagnostics()
                : _bridge.GetDiagnostics();
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

    private BridgeBackendHealth GetSelectedBackendHealth()
    {
        return SelectedTransportMode == TransportMode.UdpOpus ? _udpBackendHealth : _backendHealth;
    }

    private void UpdateBackendLabel()
    {
        var health = GetSelectedBackendHealth();
        BackendLabel = SelectedTransportMode switch
        {
            TransportMode.WebRtc => _bridge.IsNativeBackend
                ? "内部処理: WebRTC ネイティブ接続モジュール"
                : health.IsDevelopmentStub
                    ? "内部処理: WebRTC 開発用スタブ"
                    : "内部処理: WebRTC 接続モジュール未検出",
            TransportMode.UdpOpus => _udpBridge.IsNativeBackend
                ? "内部処理: UDP + Opus ネイティブ送信モジュール"
                : health.IsDevelopmentStub
                    ? "内部処理: UDP + Opus 開発用スタブ"
                    : "内部処理: UDP + Opus 送信モジュール未検出",
            _ => "内部処理"
        };
    }

    private static ConnectionDiagnostics ToUdpDiagnostics(ConnectionDiagnostics diagnostics, UdpReceiverEndpoint endpoint)
    {
        var pathType = diagnostics.PathType != NetworkPathType.Unknown
            ? diagnostics.PathType
            : UsbTetheringDetector.ClassifyPrimaryPath();
        var selectedPairType = string.IsNullOrWhiteSpace(diagnostics.SelectedCandidatePairType)
            ? $"udp_opus -> {endpoint.DisplayName}"
            : diagnostics.SelectedCandidatePairType;
        return diagnostics with
        {
            PathType = pathType,
            SelectedCandidatePairType = selectedPairType
        };
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

    private static IUdpAudioSenderBridge CreateUdpBridge()
    {
        try
        {
            return new NativeUdpAudioSenderBridge();
        }
        catch (Exception ex)
        {
            return new StubUdpAudioSenderBridge(FormatUdpBridgeStartupReason(ex));
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

    private static string FormatUdpBridgeStartupReason(Exception ex)
    {
        return NativeUdpOpusLibraryResolver.IsNativeLoadFailure(ex)
            ? NativeUdpOpusLibraryResolver.DescribeStartupFailure(ex)
            : ex.Message;
    }

    private static BridgeBackendHealth CreateBackendHealth(IAudioTransportBackend backend)
    {
        try
        {
            return backend.GetBackendHealth();
        }
        catch (Exception ex)
        {
            return new BridgeBackendHealth(
                IsReady: false,
                IsDevelopmentStub: false,
                Message: ex.Message,
                BlockingFailureCode: FailureCode.WebRtcNegotiationFailed
            );
        }
    }

    partial void OnCurrentSetupStepChanged(SetupStep value)
    {
        NotifyComputedProperties();
    }

    partial void OnSelectedTransportModeChanged(TransportMode value)
    {
        _ = value;
        UpdateBackendLabel();
        NotifyComputedProperties();
        OnPropertyChanged(nameof(SelectedTransportModeIndex));
        OnPropertyChanged(nameof(TransportModeLabel));
        OnPropertyChanged(nameof(TransportModeDescription));
        OnPropertyChanged(nameof(SenderEntryDescription));
        OnPropertyChanged(nameof(ListenerEntryDescription));
        OnPropertyChanged(nameof(ShowManualPayloadFallback));
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
        OnPropertyChanged(nameof(PathStepTitle));
        OnPropertyChanged(nameof(PathStepDescription));
    }

    private string GetRecommendedAction()
    {
        var selectedHealth = GetSelectedBackendHealth();
        if (!selectedHealth.IsReady && CurrentSetupStep == SetupStep.Entry)
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
            SetupStep.Entry => SelectedTransportMode == TransportMode.UdpOpus
                ? "UDP + Opus では Windows のメディア音声送信だけをサポートします。Android 側で受信側を開き、接続コードを貼り付けてください。"
                : "送信側か受信側を選ぶと、画面の案内に沿って進められます。",
            SetupStep.PathDiagnosing => "同じネットワーク上で直接つなぐための準備をしています。",
            SetupStep.UdpSenderDiscovering => "Android 側の UDP + Opus 受信待機を mDNS で探しています。見つかるとそのまま送信を開始します。",
            SetupStep.SenderShowInit => SelectedTransportMode == TransportMode.UdpOpus
                ? "接続コードをコピーして Android 側に貼り付けてください。Android 側が受信先情報を返すと、そのまま送信を開始します。"
                : "接続コードをコピーして Android 側に貼り付けてください。従来どおり開始データの手動共有も使えます。",
            SetupStep.SenderVerifyCode => "両端末の6桁コードが同じなら接続してください。",
            SetupStep.ListenerScanInit => "送信側から受け取った開始データを貼り付けてください。",
            SetupStep.ListenerShowConfirm => "応答データと6桁コードを送信側へ共有して接続を待ってください。",
            _ => string.Empty
        };
    }

    private ILoopbackAudioSender EnsureWebRtcLoopbackSender()
    {
        return _webRtcLoopbackSender ??= _webRtcLoopbackSenderFactory();
    }

    private ILoopbackAudioSender EnsureUdpLoopbackSender()
    {
        return _udpLoopbackSender ??= _udpLoopbackSenderFactory();
    }

    private static void StopLoopbackSender(ILoopbackAudioSender? sender)
    {
        if (sender is not null && sender.IsRunning)
        {
            sender.Stop();
        }
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

    public bool ShowManualPayloadFallback => SelectedTransportMode == TransportMode.WebRtc;
}

public enum SetupStep
{
    Entry,
    PathDiagnosing,
    UdpSenderDiscovering,
    ListenerScanInit,
    SenderShowInit,
    SenderVerifyCode,
    ListenerShowConfirm
}
