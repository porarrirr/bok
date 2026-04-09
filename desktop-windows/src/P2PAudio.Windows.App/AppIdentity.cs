using System.Reflection;

namespace P2PAudio.Windows.App;

internal static class AppIdentity
{
    internal const string BaseName = "音声リンク";
    internal const string PlatformSuffix = "Windows";

    internal static string BuildTimestamp => typeof(AppIdentity).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "BuildTimestamp")
        ?.Value
        ?? "unknown";

    internal static string WindowTitle => $"{BaseName} ({PlatformSuffix}) - Build {BuildTimestamp}";

    internal static string HeaderTitle => $"{BaseName} Build {BuildTimestamp}";
}
