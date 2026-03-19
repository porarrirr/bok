using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

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
            if (string.IsNullOrWhiteSpace(programsPath))
            {
                Debug.WriteLine("Start menu programs path is unavailable.");
                return;
            }

            var shortcutPath = Path.Combine(programsPath, ShortcutName);
            var executablePath = GetExecutablePath();
            if (!IsExecutablePath(executablePath) || !File.Exists(executablePath))
            {
                Debug.WriteLine("Current executable path is unavailable.");
                return;
            }

            var desiredShortcut = CreateDesiredShortcut(shortcutPath, executablePath);
            var existingShortcut = TryReadShortcut(shortcutPath);

            if (!ShouldUpdateShortcut(existingShortcut, desiredShortcut))
            {
                return;
            }

            CreateOrUpdateShortcut(desiredShortcut);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to ensure Start Menu shortcut: {ex}");
        }
    }

    internal static ShortcutDefinition CreateDesiredShortcut(string shortcutPath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var normalizedTargetPath = NormalizePath(targetPath)
            ?? throw new InvalidOperationException($"Could not normalize shortcut target '{targetPath}'.");
        var workingDirectory = Path.GetDirectoryName(normalizedTargetPath);

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException($"Could not determine a working directory for '{normalizedTargetPath}'.");
        }

        return new ShortcutDefinition(
            ShortcutPath: shortcutPath,
            TargetPath: normalizedTargetPath,
            WorkingDirectory: workingDirectory,
            IconLocation: $"{normalizedTargetPath},0",
            Arguments: string.Empty,
            Description: AppName
        );
    }

    internal static bool ShouldUpdateShortcut(ShortcutSnapshot? existingShortcut, ShortcutDefinition desiredShortcut)
    {
        ArgumentNullException.ThrowIfNull(desiredShortcut);

        if (existingShortcut is null)
        {
            return true;
        }

        if (!PathPointsToExistingFile(existingShortcut.TargetPath))
        {
            return true;
        }

        return !PathsMatch(existingShortcut.TargetPath, desiredShortcut.TargetPath) ||
               !PathsMatch(existingShortcut.WorkingDirectory, desiredShortcut.WorkingDirectory) ||
               !string.Equals(existingShortcut.Arguments?.Trim(), desiredShortcut.Arguments, StringComparison.Ordinal) ||
               !string.Equals(existingShortcut.Description?.Trim(), desiredShortcut.Description, StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(NormalizeIconLocation(existingShortcut.IconLocation), NormalizeIconLocation(desiredShortcut.IconLocation), StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (IsExecutablePath(processPath))
        {
            return processPath;
        }

        using var process = Process.GetCurrentProcess();
        var mainModulePath = process.MainModule?.FileName;
        if (IsExecutablePath(mainModulePath))
        {
            return mainModulePath;
        }

        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (IsExecutablePath(assemblyLocation))
        {
            return assemblyLocation;
        }

        return null;
    }

    private static bool IsExecutablePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);

    private static ShortcutSnapshot? TryReadShortcut(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
        {
            return null;
        }

        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = CreateShell();
            shortcut = OpenShortcut(shell, shortcutPath);

            return new ShortcutSnapshot(
                TargetPath: GetShortcutProperty(shortcut, nameof(ShortcutSnapshot.TargetPath)),
                WorkingDirectory: GetShortcutProperty(shortcut, nameof(ShortcutSnapshot.WorkingDirectory)),
                IconLocation: GetShortcutProperty(shortcut, nameof(ShortcutSnapshot.IconLocation)),
                Arguments: GetShortcutProperty(shortcut, nameof(ShortcutSnapshot.Arguments)),
                Description: GetShortcutProperty(shortcut, nameof(ShortcutSnapshot.Description))
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to inspect Start Menu shortcut '{shortcutPath}': {ex}");
            return null;
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void CreateOrUpdateShortcut(ShortcutDefinition shortcutDefinition)
    {
        var shortcutDirectory = Path.GetDirectoryName(shortcutDefinition.ShortcutPath);
        if (string.IsNullOrWhiteSpace(shortcutDirectory))
        {
            throw new InvalidOperationException($"Could not determine a shortcut directory for '{shortcutDefinition.ShortcutPath}'.");
        }

        Directory.CreateDirectory(shortcutDirectory);

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = CreateShell();
            shortcut = OpenShortcut(shell, shortcutDefinition.ShortcutPath);
            SetShortcutProperty(shortcut, nameof(ShortcutSnapshot.TargetPath), shortcutDefinition.TargetPath);
            SetShortcutProperty(shortcut, nameof(ShortcutSnapshot.WorkingDirectory), shortcutDefinition.WorkingDirectory);
            SetShortcutProperty(shortcut, nameof(ShortcutSnapshot.IconLocation), shortcutDefinition.IconLocation);
            SetShortcutProperty(shortcut, nameof(ShortcutSnapshot.Arguments), shortcutDefinition.Arguments);
            SetShortcutProperty(shortcut, nameof(ShortcutSnapshot.Description), shortcutDefinition.Description);
            SaveShortcut(shortcut);
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static object CreateShell()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell COM type is unavailable.");

        return Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Failed to create the WScript.Shell COM object.");
    }

    private static object OpenShortcut(object shell, string shortcutPath) =>
        shell.GetType().InvokeMember(
            "CreateShortcut",
            BindingFlags.InvokeMethod,
            binder: null,
            target: shell,
            args: [shortcutPath]) ??
        throw new InvalidOperationException($"Failed to open shortcut '{shortcutPath}'.");

    private static string? GetShortcutProperty(object shortcut, string propertyName) =>
        shortcut.GetType().InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target: shortcut,
            args: null) as string;

    private static void SetShortcutProperty(object shortcut, string propertyName, string value)
    {
        shortcut.GetType().InvokeMember(
            propertyName,
            BindingFlags.SetProperty,
            binder: null,
            target: shortcut,
            args: [value]);
    }

    private static void SaveShortcut(object shortcut)
    {
        shortcut.GetType().InvokeMember(
            "Save",
            BindingFlags.InvokeMethod,
            binder: null,
            target: shortcut,
            args: null);
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            _ = Marshal.FinalReleaseComObject(comObject);
        }
    }

    private static bool PathPointsToExistingFile(string? path) =>
        NormalizePath(path) is { } normalizedPath && File.Exists(normalizedPath);

    private static bool PathsMatch(string? left, string? right) =>
        NormalizePath(left) is { } normalizedLeft &&
        NormalizePath(right) is { } normalizedRight &&
        string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeIconLocation(string? iconLocation)
    {
        if (string.IsNullOrWhiteSpace(iconLocation))
        {
            return string.Empty;
        }

        var parts = iconLocation.Split(',', 2, StringSplitOptions.TrimEntries);
        var normalizedPath = NormalizePath(parts[0]);
        if (normalizedPath is null)
        {
            return string.Empty;
        }

        var iconIndex = parts.Length > 1 && int.TryParse(parts[1], out var parsedIndex)
            ? parsedIndex
            : 0;

        return $"{normalizedPath},{iconIndex}";
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    internal sealed record ShortcutDefinition(
        string ShortcutPath,
        string TargetPath,
        string WorkingDirectory,
        string IconLocation,
        string Arguments,
        string Description);

    internal sealed record ShortcutSnapshot(
        string? TargetPath,
        string? WorkingDirectory,
        string? IconLocation,
        string? Arguments,
        string? Description);
}
