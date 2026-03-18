using System.Reflection;
using System.Runtime.InteropServices;

namespace P2PAudio.Windows.App.Services;

internal static class NativeWebRtcLibraryResolver
{
    private const string DllBaseName = "p2paudio_core_webrtc";
    private const string DllFileName = $"{DllBaseName}.dll";
    private static readonly string[] RequiredRuntimeFiles =
    [
        "p2paudio_core_webrtc.dll",
        "datachannel.dll",
        "juice.dll",
        "legacy.dll",
        "libcrypto-3-x64.dll",
        "libssl-3-x64.dll",
        "srtp2.dll",
        "zlib1.dll"
    ];

    private static readonly object Sync = new();
    private static bool _resolverRegistered;
    private static nint _resolvedHandle;

    public static void EnsureRegistered()
    {
        if (_resolverRegistered)
        {
            return;
        }

        lock (Sync)
        {
            if (_resolverRegistered)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(NativeWebRtcLibraryResolver).Assembly, Resolve);
            _resolverRegistered = true;
        }
    }

    internal static bool IsNativeLoadFailure(Exception exception)
    {
        var unwrapped = Unwrap(exception);
        return unwrapped is DllNotFoundException or BadImageFormatException ||
               unwrapped.Message.Contains(DllBaseName, StringComparison.OrdinalIgnoreCase);
    }

    internal static string DescribeStartupFailure(Exception exception, string? baseDirectory = null)
    {
        var unwrapped = Unwrap(exception);
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var runtimeDirectory = Path.Combine(root, "runtimes", "win-x64", "native");

        if (!Directory.Exists(runtimeDirectory))
        {
            return $"ネイティブ DLL フォルダーが見つかりません: {runtimeDirectory}。{unwrapped.Message}";
        }

        var missingFiles = RequiredRuntimeFiles
            .Where(fileName => !File.Exists(Path.Combine(runtimeDirectory, fileName)))
            .ToArray();

        if (missingFiles.Length > 0)
        {
            return $"必要なネイティブ DLL が不足しています: {string.Join(", ", missingFiles)}。配置先: {runtimeDirectory}。{unwrapped.Message}";
        }

        return $"ネイティブ DLL は見つかりましたが読み込めませんでした。配置先: {runtimeDirectory}。{unwrapped.Message}";
    }

    internal static string? ResolveLibraryPath(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(root, "runtimes", "win-x64", "native", DllFileName),
            Path.Combine(root, DllFileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;

        if (!string.Equals(libraryName, DllBaseName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(libraryName, DllFileName, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        lock (Sync)
        {
            if (_resolvedHandle != 0)
            {
                return _resolvedHandle;
            }

            var libraryPath = ResolveLibraryPath();
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                return 0;
            }

            try
            {
                _resolvedHandle = NativeLibrary.Load(libraryPath);
                return _resolvedHandle;
            }
            catch
            {
                return 0;
            }
        }
    }

    private static Exception Unwrap(Exception exception)
    {
        if (exception is TypeInitializationException typeInitialization)
        {
            var inner = typeInitialization.InnerException;
            if (inner is not null)
            {
                return Unwrap(inner);
            }
        }

        return exception;
    }
}
