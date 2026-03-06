using Microsoft.UI.Xaml;
using P2PAudio.Windows.App.ViewModels;

namespace P2PAudio.Windows.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        ViewModel = new MainViewModel();
        InitializeComponent();
        Closed += OnClosed;
    }

    public MainViewModel ViewModel { get; }

    private async void OnStartSenderClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartSenderAsync();
    }

    private void OnStartListenerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.StartListener();
    }

    private async void OnProcessPayloadClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ProcessInputPayloadAsync(ViewModel.CurrentPayload);
    }

    private async void OnPastePayloadClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.PasteFromClipboardAsync();
    }

    private async void OnScanFromCameraClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ScanFromCameraAsync();
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

    private void OnClosed(object sender, WindowEventArgs args)
    {
        ViewModel.Shutdown();
    }
}
