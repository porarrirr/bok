using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using P2PAudio.Windows.Core.Models;
using P2PAudio.Windows.Core.Networking;
using P2PAudio.Windows.Core.Protocol;

namespace P2PAudio.Windows.App.Services;

public sealed class ConnectionCodeSessionFactory : IConnectionCodeSessionFactory
{
    public IConnectionCodeSession Create(string initPayload, string localAddressHintSource, long expiresAtUnixMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initPayload);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expiresAtUnixMs);

        var token = CreateRandomToken();
        foreach (var address in ResolveCandidateAddresses(localAddressHintSource))
        {
            try
            {
                var listener = new TcpListener(address, 0);
                listener.Start();
                return new ConnectionCodeSession(
                    listener: listener,
                    initPayload: initPayload,
                    payload: new ConnectionCodePayload(
                        Host: address.ToString(),
                        Port: ((IPEndPoint)listener.LocalEndpoint).Port,
                        Token: token,
                        ExpiresAtUnixMs: expiresAtUnixMs
                    )
                );
            }
            catch (SocketException)
            {
            }
        }

        throw new SessionFailure(
            FailureCode.NetworkInterfaceNotUsable,
            "接続コードを公開できるローカルIPが見つかりませんでした。Wi-Fi または USB テザリングを確認してください。"
        );
    }

    private static string CreateRandomToken()
    {
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static IReadOnlyList<IPAddress> ResolveCandidateAddresses(string offerSdp)
    {
        var preferredPath = UsbTetheringDetector.ClassifyPrimaryPath();
        var ranked = ExtractOfferCandidateAddresses(offerSdp)
            .Concat(EnumerateInterfaceAddresses())
            .Distinct()
            .OrderByDescending(address => UsbTetheringDetector.ClassifyFromCandidateAddress(address.ToString()) == preferredPath)
            .ThenBy(address => address.ToString(), StringComparer.Ordinal)
            .ToArray();

        return ranked;
    }

    private static IEnumerable<IPAddress> ExtractOfferCandidateAddresses(string offerSdp)
    {
        foreach (var rawLine in offerSdp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!rawLine.Contains("candidate:", StringComparison.OrdinalIgnoreCase) ||
                !rawLine.Contains(" typ host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidateLine = rawLine.StartsWith("a=", StringComparison.Ordinal)
                ? rawLine[2..]
                : rawLine;

            var address = UsbTetheringDetector.ExtractCandidateAddress(candidateLine);
            if (IPAddress.TryParse(address, out var ipAddress) &&
                ipAddress.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(ipAddress))
            {
                yield return ipAddress;
            }
        }
    }

    private static IEnumerable<IPAddress> EnumerateInterfaceAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));
    }

    private sealed class ConnectionCodeSession : IConnectionCodeSession
    {
        private readonly TcpListener _listener;
        private readonly string _initPayload;
        private readonly string _token;
        private readonly CancellationTokenSource _disposeCts = new();
        private readonly TaskCompletionSource<ConnectionCodeSubmission> _confirmPayloadTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _acceptLoopTask;
        private bool _disposed;

        public ConnectionCodeSession(TcpListener listener, string initPayload, ConnectionCodePayload payload)
        {
            _listener = listener;
            _initPayload = initPayload;
            _token = payload.Token;
            ExpiresAtUnixMs = payload.ExpiresAtUnixMs;
            ConnectionCode = ConnectionCodeCodec.Encode(payload);
            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_disposeCts.Token));
        }

        public string ConnectionCode { get; }

        public long ExpiresAtUnixMs { get; }

        public Task<ConnectionCodeSubmission> WaitForConfirmPayloadAsync(CancellationToken cancellationToken)
        {
            return _confirmPayloadTcs.Task.WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _disposeCts.Cancel();
            _listener.Stop();
            _confirmPayloadTcs.TrySetCanceled(_disposeCts.Token);
            _disposeCts.Dispose();
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    client?.Dispose();
                    break;
                }
                catch (ObjectDisposedException)
                {
                    client?.Dispose();
                    break;
                }
                catch (SocketException)
                {
                    client?.Dispose();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                using var stream = client.GetStream();

                try
                {
                    var request = await ReadRequestAsync(stream, cancellationToken);
                    var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    if (nowUnixMs > ExpiresAtUnixMs)
                    {
                        await WriteResponseAsync(stream, 410, "Gone", "expired", cancellationToken);
                        return;
                    }

                    if (!string.Equals(request.Token, _token, StringComparison.Ordinal))
                    {
                        await WriteResponseAsync(stream, 401, "Unauthorized", "invalid_token", cancellationToken);
                        return;
                    }

                    if (request.Method == "GET" && request.Path == "/pairing/init")
                    {
                        await WriteResponseAsync(stream, 200, "OK", _initPayload, cancellationToken);
                        return;
                    }

                    if (request.Method == "POST" && request.Path == "/pairing/confirm")
                    {
                        if (string.IsNullOrWhiteSpace(request.Body))
                        {
                            await WriteResponseAsync(stream, 400, "Bad Request", "missing_body", cancellationToken);
                            return;
                        }

                        var remoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;
                        if (!_confirmPayloadTcs.TrySetResult(new ConnectionCodeSubmission(
                            Payload: request.Body,
                            RemoteAddress: remoteAddress
                        )))
                        {
                            await WriteResponseAsync(stream, 409, "Conflict", "already_confirmed", cancellationToken);
                            return;
                        }

                        await WriteResponseAsync(stream, 202, "Accepted", "ok", cancellationToken);
                        return;
                    }

                    await WriteResponseAsync(stream, 404, "Not Found", "not_found", cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await WriteResponseAsync(stream, 500, "Internal Server Error", "internal_error", cancellationToken);
                    }
                }
            }
        }

        private static async Task<IncomingRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(
                stream,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true
            );

            var requestLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                throw new InvalidOperationException("Missing HTTP request line.");
            }

            var requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length < 2)
            {
                throw new InvalidOperationException("Invalid HTTP request line.");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var headerLine = await reader.ReadLineAsync(cancellationToken);
                if (headerLine is null)
                {
                    throw new InvalidOperationException("Unexpected end of HTTP headers.");
                }
                if (headerLine.Length == 0)
                {
                    break;
                }

                var separatorIndex = headerLine.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                headers[headerLine[..separatorIndex].Trim()] = headerLine[(separatorIndex + 1)..].Trim();
            }

            var target = requestParts[1];
            var uri = new Uri($"http://local{target}", UriKind.Absolute);
            var token = ExtractQueryValue(uri.Query, "token");
            var contentLength = 0;
            if (headers.TryGetValue("Content-Length", out var contentLengthRaw))
            {
                int.TryParse(contentLengthRaw, out contentLength);
            }

            var body = string.Empty;
            if (contentLength > 0)
            {
                var bodyBuffer = new char[contentLength];
                var totalRead = 0;
                while (totalRead < contentLength)
                {
                    var read = await reader.ReadBlockAsync(
                        bodyBuffer.AsMemory(totalRead, contentLength - totalRead),
                        cancellationToken
                    );
                    if (read == 0)
                    {
                        break;
                    }

                    totalRead += read;
                }

                body = new string(bodyBuffer, 0, totalRead);
            }

            return new IncomingRequest(
                Method: requestParts[0].ToUpperInvariant(),
                Path: uri.AbsolutePath,
                Token: token,
                Body: body
            );
        }

        private static string ExtractQueryValue(string query, string key)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return string.Empty;
            }

            var trimmed = query.TrimStart('?');
            foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length == 0)
                {
                    continue;
                }

                if (!string.Equals(Uri.UnescapeDataString(pieces[0]), key, StringComparison.Ordinal))
                {
                    continue;
                }

                return pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1]) : string.Empty;
            }

            return string.Empty;
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            int statusCode,
            string statusText,
            string body,
            CancellationToken cancellationToken)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                         "Content-Type: text/plain; charset=utf-8\r\n" +
                         $"Content-Length: {bodyBytes.Length}\r\n" +
                         "Connection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);

            await stream.WriteAsync(headerBytes, cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
        }

        private sealed record IncomingRequest(string Method, string Path, string Token, string Body);
    }
}
