using System.Reflection;
using System.Runtime.InteropServices;

namespace P2PAudio.Windows.App.Services;

internal static class NativeWebRtcLibraryResolver
{
    private const string DllBaseName = "p2paudio_core_webrtc";
    private const string DllFileName = $"{DllBaseName}.dll";
    private static readonly NativeLibrarySpec WebRtcLibrary = new(
        BaseName: DllBaseName,
        FileName: DllFileName,
        RequiredRuntimeFiles:
        [
            "p2paudio_core_webrtc.dll",
            "datachannel.dll",
            "juice.dll",
            "legacy.dll",
            "libcrypto-3-x64.dll",
            "libssl-3-x64.dll",
            "srtp2.dll",
            "zlib1.dll"
        ]
    );
    private static readonly NativeLibrarySpec UdpOpusLibrary = new(
        BaseName: NativeUdpOpusLibraryResolver.DllBaseName,
        FileName: NativeUdpOpusLibraryResolver.DllFileName,
        RequiredRuntimeFiles:
        [
            NativeUdpOpusLibraryResolver.DllFileName,
            "opus.dll",
            "portaudio.dll"
        ]
    );
    private static readonly NativeLibrarySpec[] Libraries = [WebRtcLibrary, UdpOpusLibrary];

    private static readonly object Sync = new();
    private static bool _resolverRegistered;
    private static readonly Dictionary<string, nint> ResolvedHandles = new(StringComparer.OrdinalIgnoreCase);

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
               Libraries.Any(spec => unwrapped.Message.Contains(spec.BaseName, StringComparison.OrdinalIgnoreCase));
    }

    internal static string DescribeStartupFailure(Exception exception, string? baseDirectory = null)
        => DescribeStartupFailure(WebRtcLibrary.BaseName, exception, baseDirectory);

    internal static string DescribeStartupFailure(string dllBaseName, Exception exception, string? baseDirectory = null)
    {
        var unwrapped = Unwrap(exception);
        var library = GetLibrarySpec(dllBaseName);
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var runtimeDirectory = Path.Combine(root, "runtimes", "win-x64", "native");

        if (!Directory.Exists(runtimeDirectory))
        {
            return $"ネイティブ DLL フォルダーが見つかりません: {runtimeDirectory}。{unwrapped.Message}";
        }

        var missingFiles = library.RequiredRuntimeFiles
            .Where(fileName => !File.Exists(Path.Combine(runtimeDirectory, fileName)))
            .ToArray();

        if (missingFiles.Length > 0)
        {
            return $"必要なネイティブ DLL が不足しています: {string.Join(", ", missingFiles)}。配置先: {runtimeDirectory}。{unwrapped.Message}";
        }

        return $"ネイティブ DLL は見つかりましたが読み込めませんでした。配置先: {runtimeDirectory}。{unwrapped.Message}";
    }

    internal static string? ResolveLibraryPath(string? baseDirectory = null)
        => ResolveLibraryPath(WebRtcLibrary.BaseName, baseDirectory);

    internal static string? ResolveLibraryPath(string dllBaseName, string? baseDirectory = null)
    {
        var library = GetLibrarySpec(dllBaseName);
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(root, "runtimes", "win-x64", "native", library.FileName),
            Path.Combine(root, library.FileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;

        var library = Libraries.FirstOrDefault(spec =>
            string.Equals(libraryName, spec.BaseName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(libraryName, spec.FileName, StringComparison.OrdinalIgnoreCase));
        if (library is null)
        {
            return 0;
        }

        lock (Sync)
        {
            if (ResolvedHandles.TryGetValue(library.BaseName, out var resolvedHandle) && resolvedHandle != 0)
            {
                return resolvedHandle;
            }

            var libraryPath = ResolveLibraryPath(library.BaseName);
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                return 0;
            }

            try
            {
                resolvedHandle = NativeLibrary.Load(libraryPath);
                ResolvedHandles[library.BaseName] = resolvedHandle;
                return resolvedHandle;
            }
            catch
            {
                return 0;
            }
        }
    }

    private static NativeLibrarySpec GetLibrarySpec(string dllBaseName)
    {
        return Libraries.First(spec => string.Equals(spec.BaseName, dllBaseName, StringComparison.OrdinalIgnoreCase));
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

    private sealed record NativeLibrarySpec(
        string BaseName,
        string FileName,
        IReadOnlyList<string> RequiredRuntimeFiles
    );
}
