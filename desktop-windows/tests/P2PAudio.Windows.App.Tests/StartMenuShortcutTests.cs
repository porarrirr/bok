using P2PAudio.Windows.App;

namespace P2PAudio.Windows.App.Tests;

public sealed class StartMenuShortcutTests
{
    [Fact]
    public void CreateDesiredShortcut_UsesExecutableDirectoryForLaunchMetadata()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var shortcutPath = Path.Combine(tempDirectory, "P2PAudio.lnk");
            var executablePath = Path.Combine(tempDirectory, "bin", "..", "bin", "P2PAudio.Windows.App.exe");

            var definition = StartMenuShortcut.CreateDesiredShortcut(shortcutPath, executablePath);

            var normalizedExecutablePath = Path.GetFullPath(executablePath);
            Assert.Equal(shortcutPath, definition.ShortcutPath);
            Assert.Equal(normalizedExecutablePath, definition.TargetPath);
            Assert.Equal(Path.GetDirectoryName(normalizedExecutablePath), definition.WorkingDirectory);
            Assert.Equal($"{normalizedExecutablePath},0", definition.IconLocation);
            Assert.Equal(string.Empty, definition.Arguments);
            Assert.Equal("P2PAudio", definition.Description);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ShouldUpdateShortcut_WhenShortcutIsMissing_ReturnsTrue()
    {
        var definition = StartMenuShortcut.CreateDesiredShortcut(
            shortcutPath: @"C:\Temp\P2PAudio.lnk",
            targetPath: @"C:\Temp\P2PAudio.Windows.App.exe");

        Assert.True(StartMenuShortcut.ShouldUpdateShortcut(existingShortcut: null, desiredShortcut: definition));
    }

    [Fact]
    public void ShouldUpdateShortcut_WhenExistingTargetDoesNotExist_ReturnsTrue()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var desiredTargetPath = Path.Combine(tempDirectory, "P2PAudio.Windows.App.exe");
            File.WriteAllBytes(desiredTargetPath, []);

            var definition = StartMenuShortcut.CreateDesiredShortcut(
                shortcutPath: Path.Combine(tempDirectory, "P2PAudio.lnk"),
                targetPath: desiredTargetPath);

            var existingShortcut = new StartMenuShortcut.ShortcutSnapshot(
                TargetPath: Path.Combine(tempDirectory, "missing", "P2PAudio.Windows.App.exe"),
                WorkingDirectory: definition.WorkingDirectory,
                IconLocation: definition.IconLocation,
                Arguments: definition.Arguments,
                Description: definition.Description);

            Assert.True(StartMenuShortcut.ShouldUpdateShortcut(existingShortcut, definition));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ShouldUpdateShortcut_WhenMetadataAlreadyMatches_ReturnsFalse()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var desiredTargetPath = Path.Combine(tempDirectory, "P2PAudio.Windows.App.exe");
            File.WriteAllBytes(desiredTargetPath, []);

            var definition = StartMenuShortcut.CreateDesiredShortcut(
                shortcutPath: Path.Combine(tempDirectory, "P2PAudio.lnk"),
                targetPath: desiredTargetPath);

            var existingShortcut = new StartMenuShortcut.ShortcutSnapshot(
                TargetPath: definition.TargetPath.ToUpperInvariant(),
                WorkingDirectory: definition.WorkingDirectory.ToUpperInvariant(),
                IconLocation: $"{definition.TargetPath.ToUpperInvariant()},0",
                Arguments: definition.Arguments,
                Description: definition.Description.ToLowerInvariant());

            Assert.False(StartMenuShortcut.ShouldUpdateShortcut(existingShortcut, definition));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ShouldUpdateShortcut_WhenWorkingDirectoryDiffers_ReturnsTrue()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var desiredTargetPath = Path.Combine(tempDirectory, "P2PAudio.Windows.App.exe");
            File.WriteAllBytes(desiredTargetPath, []);

            var definition = StartMenuShortcut.CreateDesiredShortcut(
                shortcutPath: Path.Combine(tempDirectory, "P2PAudio.lnk"),
                targetPath: desiredTargetPath);

            var existingShortcut = new StartMenuShortcut.ShortcutSnapshot(
                TargetPath: definition.TargetPath,
                WorkingDirectory: Path.Combine(tempDirectory, "other"),
                IconLocation: definition.IconLocation,
                Arguments: definition.Arguments,
                Description: definition.Description);

            Assert.True(StartMenuShortcut.ShouldUpdateShortcut(existingShortcut, definition));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ShouldUpdateShortcut_WhenArgumentsDiffer_ReturnsTrue()
    {
        var tempDirectory = CreateTempDirectory();

        try
        {
            var desiredTargetPath = Path.Combine(tempDirectory, "P2PAudio.Windows.App.exe");
            File.WriteAllBytes(desiredTargetPath, []);

            var definition = StartMenuShortcut.CreateDesiredShortcut(
                shortcutPath: Path.Combine(tempDirectory, "P2PAudio.lnk"),
                targetPath: desiredTargetPath);

            var existingShortcut = new StartMenuShortcut.ShortcutSnapshot(
                TargetPath: definition.TargetPath,
                WorkingDirectory: definition.WorkingDirectory,
                IconLocation: definition.IconLocation,
                Arguments: @"--app-path=""C:\Old\P2PAudio.Windows.App.exe""",
                Description: definition.Description);

            Assert.True(StartMenuShortcut.ShouldUpdateShortcut(existingShortcut, definition));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "p2paudio-shortcut-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
