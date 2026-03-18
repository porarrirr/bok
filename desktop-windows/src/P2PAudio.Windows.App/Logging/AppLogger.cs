using System.Diagnostics;
using System.Text;

namespace P2PAudio.Windows.App.Logging;

public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "P2PAudio",
        "Logs"
    );

    public static string CurrentLogFilePath => Path.Combine(
        LogDirectory,
        $"p2paudio-{DateTimeOffset.Now:yyyyMMdd}.log"
    );

    public static void D(
        string category,
        string eventName,
        string message,
        IReadOnlyDictionary<string, object?>? context = null)
        => Emit("DEBUG", category, eventName, message, context, null);

    public static void I(
        string category,
        string eventName,
        string message,
        IReadOnlyDictionary<string, object?>? context = null)
        => Emit("INFO", category, eventName, message, context, null);

    public static void W(
        string category,
        string eventName,
        string message,
        IReadOnlyDictionary<string, object?>? context = null)
        => Emit("WARN", category, eventName, message, context, null);

    public static void E(
        string category,
        string eventName,
        string message,
        IReadOnlyDictionary<string, object?>? context = null,
        Exception? exception = null)
        => Emit("ERROR", category, eventName, message, context, exception);

    private static void Emit(
        string level,
        string category,
        string eventName,
        string message,
        IReadOnlyDictionary<string, object?>? context,
        Exception? exception)
    {
        var contextPart = context is null
            ? string.Empty
            : string.Join(
                " ",
                context
                    .Where(entry => entry.Value is not null)
                    .Select(entry => $"{entry.Key}={entry.Value}")
            );

        var lineBuilder = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append(" level=").Append(level)
            .Append(" category=").Append(category)
            .Append(" event=").Append(eventName)
            .Append(" msg=").Append(message);

        if (!string.IsNullOrWhiteSpace(contextPart))
        {
            lineBuilder.Append(' ').Append(contextPart);
        }

        if (exception is not null)
        {
            lineBuilder
                .Append(" exceptionType=").Append(exception.GetType().Name)
                .Append(" exceptionMessage=").Append(exception.Message);
        }

        var line = lineBuilder.ToString();
        Debug.WriteLine(line);
        if (exception is not null)
        {
            Debug.WriteLine(exception);
        }

        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);

                using var stream = new FileStream(
                    CurrentLogFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite
                );
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.WriteLine(line);
                if (exception is not null)
                {
                    writer.WriteLine(exception);
                }
            }
        }
        catch (IOException ioException)
        {
            Debug.WriteLine($"log_write_failed path={CurrentLogFilePath} error={ioException}");
        }
        catch (UnauthorizedAccessException unauthorizedAccessException)
        {
            Debug.WriteLine($"log_write_denied path={CurrentLogFilePath} error={unauthorizedAccessException}");
        }
    }
}
