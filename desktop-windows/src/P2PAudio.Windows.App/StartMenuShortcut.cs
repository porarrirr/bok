using System.Diagnostics;
using System.Reflection;

namespace P2PAudio.Windows.App;

internal static class StartMenuShortcut
{
    private const string ShortcutName = "P2PAudio.lnk";
    private const string AppName = "P2PAudio";

    internal static void EnsureStartMenuShortcut()
    {
        try
        {
            var programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var shortcutPath = Path.Combine(programsPath, ShortcutName);

            if (File.Exists(shortcutPath))
                return;

            var exePath = GetExecutablePath();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return;

            CreateShortcut(shortcutPath, exePath);
        }
        catch
        {
        }
    }

    private static string? GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
            return processPath;

        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(assemblyLocation) && File.Exists(assemblyLocation))
            return assemblyLocation;

        return null;
    }

    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        var escapedShortcutPath = shortcutPath.Replace("'", "''");
        var escapedTargetPath = targetPath.Replace("'", "''");

        var script = $@"
$ws = New-Object -ComObject WScript.Shell
$s = $ws.CreateShortcut('{escapedShortcutPath}')
$s.TargetPath = '{escapedTargetPath}'
$s.Description = '{AppName}'
$s.Save()
";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(5000);
    }
}