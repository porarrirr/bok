using Microsoft.UI.Xaml;

namespace P2PAudio.Windows.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = args;

        if (_window is null)
        {
            _window = new MainWindow();
            _ = Task.Run(StartMenuShortcut.EnsureStartMenuShortcut);
        }

        _window.Present();
    }
}
