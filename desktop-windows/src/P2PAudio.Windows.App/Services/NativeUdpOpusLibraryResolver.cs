namespace P2PAudio.Windows.App.Services;

internal static class NativeUdpOpusLibraryResolver
{
    internal const string DllBaseName = "p2paudio_core_udp_opus";
    internal const string DllFileName = $"{DllBaseName}.dll";

    public static void EnsureRegistered()
    {
        NativeWebRtcLibraryResolver.EnsureRegistered();
    }

    internal static bool IsNativeLoadFailure(Exception exception)
    {
        return NativeWebRtcLibraryResolver.IsNativeLoadFailure(exception);
    }

    internal static string DescribeStartupFailure(Exception exception, string? baseDirectory = null)
    {
        return NativeWebRtcLibraryResolver.DescribeStartupFailure(DllBaseName, exception, baseDirectory);
    }

    internal static string? ResolveLibraryPath(string? baseDirectory = null)
    {
        return NativeWebRtcLibraryResolver.ResolveLibraryPath(DllBaseName, baseDirectory);
    }
}
