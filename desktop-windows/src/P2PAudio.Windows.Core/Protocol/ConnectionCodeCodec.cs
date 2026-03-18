using P2PAudio.Windows.Core.Models;

namespace P2PAudio.Windows.Core.Protocol;

public static class ConnectionCodeCodec
{
    public const string Prefix = "p2paudio-c1:";

    public static bool LooksLikeConnectionCode(string? raw)
    {
        return !string.IsNullOrWhiteSpace(raw) &&
               raw.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public static string Encode(ConnectionCodePayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.Token);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payload.Port);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payload.ExpiresAtUnixMs);

        return $"{Prefix}{payload.Host}:{payload.Port}:{payload.ExpiresAtUnixMs}:{payload.Token}";
    }

    public static ConnectionCodePayload Decode(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        if (!raw.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Connection code prefix is invalid.", nameof(raw));
        }

        var body = raw[Prefix.Length..];
        var parts = body.Split(':', 4, StringSplitOptions.None);
        if (parts.Length != 4)
        {
            throw new ArgumentException("Connection code format is invalid.", nameof(raw));
        }

        var host = parts[0].Trim();
        var portRaw = parts[1].Trim();
        var expiresAtRaw = parts[2].Trim();
        var token = parts[3].Trim();

        if (string.IsNullOrWhiteSpace(host) ||
            !int.TryParse(portRaw, out var port) ||
            port <= 0 ||
            port > ushort.MaxValue ||
            !long.TryParse(expiresAtRaw, out var expiresAtUnixMs) ||
            expiresAtUnixMs <= 0 ||
            string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Connection code payload is invalid.", nameof(raw));
        }

        return new ConnectionCodePayload(
            Host: host,
            Port: port,
            Token: token,
            ExpiresAtUnixMs: expiresAtUnixMs
        );
    }
}
