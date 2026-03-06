using System.IO.Compression;
using System.Text;
using System.Text.Json;
using P2PAudio.Windows.Core.Models;

namespace P2PAudio.Windows.Core.Protocol;

public static class QrPayloadCodec
{
    private const string CompressedPrefix = "p2paudio-z1:";
    private const int MinBytesForCompression = 256;
    private const int MaxDecompressedBytes = 512_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    public static string EncodeInit(PairingInitPayload payload)
    {
        var raw = JsonSerializer.Serialize(payload, JsonOptions);
        return EncodeTransportString(raw);
    }

    public static string EncodeConfirm(PairingConfirmPayload payload)
    {
        var raw = JsonSerializer.Serialize(payload, JsonOptions);
        return EncodeTransportString(raw);
    }

    public static PairingInitPayload DecodeInit(string raw)
    {
        var normalized = DecodeTransportString(raw);
        var payload = JsonSerializer.Deserialize<PairingInitPayload>(normalized, JsonOptions);
        return payload ?? throw new SessionFailure(FailureCode.InvalidPayload, "Invalid init payload");
    }

    public static PairingConfirmPayload DecodeConfirm(string raw)
    {
        var normalized = DecodeTransportString(raw);
        var payload = JsonSerializer.Deserialize<PairingConfirmPayload>(normalized, JsonOptions);
        return payload ?? throw new SessionFailure(FailureCode.InvalidPayload, "Invalid confirm payload");
    }

    private static string EncodeTransportString(string raw)
    {
        var utf8 = Encoding.UTF8.GetBytes(raw);
        if (utf8.Length < MinBytesForCompression)
        {
            return raw;
        }

        var compressed = Compress(utf8);
        if (compressed is null)
        {
            return raw;
        }
        var encoded = ToBase64Url(compressed);
        if (encoded.Length >= raw.Length)
        {
            return raw;
        }
        return $"{CompressedPrefix}{encoded}";
    }

    private static string DecodeTransportString(string raw)
    {
        if (!raw.StartsWith(CompressedPrefix, StringComparison.Ordinal))
        {
            return raw;
        }

        var encoded = raw[CompressedPrefix.Length..];
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new SessionFailure(FailureCode.InvalidPayload, "Compressed payload is empty");
        }

        byte[] compressed;
        try
        {
            compressed = FromBase64Url(encoded);
        }
        catch (FormatException)
        {
            throw new SessionFailure(FailureCode.InvalidPayload, "Compressed payload base64 is invalid");
        }

        var decompressed = Decompress(compressed, MaxDecompressedBytes);
        if (decompressed is null)
        {
            throw new SessionFailure(FailureCode.InvalidPayload, "Failed to decode compressed payload");
        }
        return Encoding.UTF8.GetString(decompressed);
    }

    private static byte[]? Compress(byte[] input)
    {
        try
        {
            using var output = new MemoryStream();
            using (var compressor = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                compressor.Write(input, 0, input.Length);
            }
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? Decompress(byte[] input, int maxBytes)
    {
        try
        {
            using var source = new MemoryStream(input);
            using var inflater = new ZLibStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream(input.Length * 2);
            var buffer = new byte[1024];
            while (true)
            {
                var read = inflater.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                output.Write(buffer, 0, read);
                if (output.Length > maxBytes)
                {
                    return null;
                }
            }
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] FromBase64Url(string encoded)
    {
        var normalized = encoded
            .Replace('-', '+')
            .Replace('_', '/');
        var remainder = normalized.Length % 4;
        if (remainder > 0)
        {
            normalized = normalized.PadRight(normalized.Length + (4 - remainder), '=');
        }
        return Convert.FromBase64String(normalized);
    }
}
