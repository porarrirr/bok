using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using P2PAudio.Windows.App.ViewModels;
using P2PAudio.Windows.Core.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WinRT.Interop;

namespace P2PAudio.Windows.App;

public sealed partial class MainWindow : Window
{
    private const int SwShow = 5;
    private const int SwRestore = 9;

    private DispatcherTimer? _transientTimer;
    private bool _startupInitialized;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    public MainWindow()
    {
        ViewModel = new MainViewModel(initializeImmediately: false);
        InitializeComponent();
        TryApplySystemBackdrop();
        Activated += OnActivated;
        Closed += OnClosed;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateFlowCards();
    }

    public MainViewModel ViewModel { get; }

    public void Present()
    {
        Activate();
        EnsureWindowVisible();
        _ = DispatcherQueue.TryEnqueue(EnsureWindowVisible);
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        _ = args;
        EnsureWindowVisible();
        if (_startupInitialized)
        {
            return;
        }

        _startupInitialized = true;
        Activated -= OnActivated;

        await Task.Yield();
        await ViewModel.InitializeAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => OnViewModelPropertyChanged(sender, e));
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.CurrentSetupStep) or nameof(MainViewModel.CurrentStreamState) or nameof(MainViewModel.IsVerificationPending) or nameof(MainViewModel.FailureHintLabel))
        {
            UpdateFlowCards();
        }
        if (e.PropertyName == nameof(MainViewModel.ActiveSessionId))
        {
            SessionIdPanel.Visibility = string.IsNullOrEmpty(ViewModel.ActiveSessionId)
                ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void UpdateFlowCards()
    {
        var step = ViewModel.CurrentSetupStep;
        var streamState = ViewModel.CurrentStreamState;
        var showEntryCard = step == SetupStep.Entry || streamState == StreamState.Failed;
        var hideSetupCards = streamState is StreamState.Streaming or StreamState.Interrupted or StreamState.Ended or StreamState.Failed;

        EntryCard.Visibility = showEntryCard ? Visibility.Visible : Visibility.Collapsed;
        PathDiagnosingCard.Visibility = !hideSetupCards && step == SetupStep.PathDiagnosing ? Visibility.Visible : Visibility.Collapsed;
        SenderShowInitCard.Visibility = !hideSetupCards && step == SetupStep.SenderShowInit ? Visibility.Visible : Visibility.Collapsed;
        SenderVerifyCodeCard.Visibility = !hideSetupCards && step == SetupStep.SenderVerifyCode && ViewModel.IsVerificationPending
            ? Visibility.Visible
            : Visibility.Collapsed;
        ListenerScanInitCard.Visibility = !hideSetupCards && step == SetupStep.ListenerScanInit ? Visibility.Visible : Visibility.Collapsed;
        ListenerShowConfirmCard.Visibility = !hideSetupCards && step == SetupStep.ListenerShowConfirm ? Visibility.Visible : Visibility.Collapsed;

        var showFailure = streamState is StreamState.Failed or StreamState.Interrupted
            || !string.IsNullOrEmpty(ViewModel.FailureHintLabel);
        StatusFailurePanel.Visibility = showFailure ? Visibility.Visible : Visibility.Collapsed;

        UpdateStatusHeroVisual(streamState);
    }

    private void UpdateStatusHeroVisual(StreamState state)
    {
        string borderKey = state switch
        {
            StreamState.Streaming => "SystemFillColorSuccessBrush",
            StreamState.Capturing or StreamState.Connecting => "SystemFillColorAttentionBrush",
            StreamState.Interrupted => "SystemFillColorCautionBrush",
            StreamState.Failed => "SystemFillColorCriticalBrush",
            _ => "CardStrokeColorDefaultBrush"
        };

        if (Application.Current.Resources.TryGetValue(borderKey, out var brush) && brush is Brush b)
        {
            StatusHeroCard.BorderBrush = b;
        }
        else if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var fallback) && fallback is Brush fb)
        {
            StatusHeroCard.BorderBrush = fb;
        }

        StatusHeroCard.BorderThickness = state is StreamState.Streaming or StreamState.Interrupted
            or StreamState.Failed or StreamState.Capturing or StreamState.Connecting
            ? new Thickness(4, 1, 1, 1)
            : new Thickness(1);
    }

    private async void OnStartSenderClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartSenderAsync();
    }

    private void OnStartListenerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.StartListener();
    }

    private async void OnPastePayloadClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.PasteFromClipboardAsync();
    }

    private async void OnProcessSenderConfirmPayloadClick(object sender, RoutedEventArgs e)
    {
        var text = SenderConfirmPayloadInput.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            await ViewModel.ProcessInputPayloadAsync(text);
        }
    }

    private async void OnProcessListenerInitPayloadClick(object sender, RoutedEventArgs e)
    {
        var text = ListenerInitPayloadInput.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            await ViewModel.ProcessInputPayloadAsync(text);
        }
    }

    private async void OnApproveVerificationClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ApproveVerificationAndConnectAsync();
    }

    private void OnRejectVerificationClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RejectVerificationAndRestart();
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Stop();
    }

    private void OnCopyPayloadClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ViewModel.CurrentPayload))
            return;

        var dataPackage = new DataPackage();
        dataPackage.SetText(ViewModel.CurrentPayload);
        Clipboard.SetContent(dataPackage);

        ShowTransientMessage("接続データをクリップボードへコピーしました。");
    }

    private void ShowTransientMessage(string message)
    {
        TransientInfoBar.Message = message;
        TransientInfoBar.IsOpen = true;

        _transientTimer?.Stop();
        _transientTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _transientTimer.Tick += (_, _) =>
        {
            TransientInfoBar.IsOpen = false;
            _transientTimer.Stop();
        };
        _transientTimer.Start();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        ViewModel.Shutdown();
    }

    private void TryApplySystemBackdrop()
    {
        if (!MicaController.IsSupported())
        {
            return;
        }

        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
    }

    private void EnsureWindowVisible()
    {
        var appWindow = AppWindow;
        if (appWindow.Size.Width <= 0 || appWindow.Size.Height <= 0)
        {
            appWindow.Resize(new SizeInt32(980, 760));
        }
        if (!appWindow.IsVisible)
        {
            appWindow.Show();
        }

        var windowHandle = WindowNative.GetWindowHandle(this);
        if (windowHandle == nint.Zero)
        {
            return;
        }

        _ = ShowWindow(windowHandle, SwRestore);
        _ = ShowWindow(windowHandle, SwShow);
    }
}
