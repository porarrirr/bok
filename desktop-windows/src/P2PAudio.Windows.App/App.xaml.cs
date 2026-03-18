using Microsoft.UI.Xaml;
using P2PAudio.Windows.App.Logging;

namespace P2PAudio.Windows.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        AppLogger.I(
            "App",
            "app_constructed",
            "Windows app constructed",
            new Dictionary<string, object?>
            {
                ["logFile"] = AppLogger.CurrentLogFilePath
            }
        );
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = args;

        if (_window is null)
        {
            _window = new MainWindow();
            _ = Task.Run(StartMenuShortcut.EnsureStartMenuShortcut);
            AppLogger.I("App", "window_created", "Main window created");
        }

        _window.Present();
        AppLogger.I("App", "app_presented", "Main window presented");
    }
}
