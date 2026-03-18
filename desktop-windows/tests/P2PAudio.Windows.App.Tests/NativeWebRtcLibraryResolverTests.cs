using P2PAudio.Windows.App.Services;

namespace P2PAudio.Windows.App.Tests;

public sealed class NativeWebRtcLibraryResolverTests
{
    [Fact]
    public void ResolveLibraryPath_WhenBridgeExistsInRuntimeFolder_ReturnsRuntimePath()
    {
        var root = CreateTempDirectory();

        try
        {
            var runtimeDirectory = Path.Combine(root, "runtimes", "win-x64", "native");
            Directory.CreateDirectory(runtimeDirectory);
            var expectedPath = Path.Combine(runtimeDirectory, "p2paudio_core_webrtc.dll");
            File.WriteAllText(expectedPath, "stub");

            var actualPath = NativeWebRtcLibraryResolver.ResolveLibraryPath(root);

            Assert.Equal(expectedPath, actualPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DescribeStartupFailure_WhenRuntimeFolderIsMissing_MentionsExpectedFolder()
    {
        var root = CreateTempDirectory();

        try
        {
            var message = NativeWebRtcLibraryResolver.DescribeStartupFailure(
                new DllNotFoundException("missing runtime"),
                root
            );

            Assert.Contains("ネイティブ DLL フォルダーが見つかりません", message);
            Assert.Contains(Path.Combine(root, "runtimes", "win-x64", "native"), message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DescribeStartupFailure_WhenDependenciesAreMissing_ListsMissingFiles()
    {
        var root = CreateTempDirectory();

        try
        {
            var runtimeDirectory = Path.Combine(root, "runtimes", "win-x64", "native");
            Directory.CreateDirectory(runtimeDirectory);
            File.WriteAllText(Path.Combine(runtimeDirectory, "p2paudio_core_webrtc.dll"), "stub");
            File.WriteAllText(Path.Combine(runtimeDirectory, "datachannel.dll"), "stub");

            var message = NativeWebRtcLibraryResolver.DescribeStartupFailure(
                new DllNotFoundException("missing dependency"),
                root
            );

            Assert.Contains("必要なネイティブ DLL が不足しています", message);
            Assert.Contains("juice.dll", message);
            Assert.Contains("libcrypto-3-x64.dll", message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"p2paudio-native-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
