using System.Net;
using System.Net.Http;
using System.Text;
using P2PAudio.Windows.Core.Models;

namespace P2PAudio.Windows.App.Services;

public sealed class ConnectionCodeClientException : IOException
{
    public ConnectionCodeClientException(SessionFailure failure) : base(failure.Message)
    {
        Failure = failure;
    }

    public SessionFailure Failure { get; }
}

public static class ConnectionCodeClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static async Task<string> FetchInitPayloadAsync(
        ConnectionCodePayload connectionCode,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, connectionCode, "/pairing/init");
        return await RunRequestAsync(request, HttpStatusCode.OK, "fetch init payload", cancellationToken);
    }

    public static async Task SubmitConfirmPayloadAsync(
        ConnectionCodePayload connectionCode,
        string confirmPayload,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, connectionCode, "/pairing/confirm");
        request.Content = new StringContent(confirmPayload, Encoding.UTF8, "text/plain");
        _ = await RunRequestAsync(request, HttpStatusCode.Accepted, "submit confirm payload", cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        ConnectionCodePayload connectionCode,
        string path)
    {
        var token = Uri.EscapeDataString(connectionCode.Token);
        var uri = new UriBuilder(Uri.UriSchemeHttp, connectionCode.Host, connectionCode.Port, path)
        {
            Query = $"token={token}"
        }.Uri;

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.ParseAdd("text/plain");
        request.Headers.ConnectionClose = true;
        return request;
    }

    private static async Task<string> RunRequestAsync(
        HttpRequestMessage request,
        HttpStatusCode successCode,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            return await ReadResponseBodyAsync(response, successCode, operationName, cancellationToken);
        }
        catch (ConnectionCodeClientException)
        {
            throw;
        }
        catch (HttpRequestException error)
        {
            throw MapTransportFailure(error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw MapTransportFailure(error);
        }
        catch (IOException error)
        {
            throw MapTransportFailure(error);
        }
    }

    private static async Task<string> ReadResponseBodyAsync(
        HttpResponseMessage response,
        HttpStatusCode successCode,
        string operationName,
        CancellationToken cancellationToken)
    {
        var body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == successCode)
        {
            return body;
        }

        throw response.StatusCode switch
        {
            HttpStatusCode.Gone => new ConnectionCodeClientException(
                new SessionFailure(FailureCode.SessionExpired, $"Connection code expired during {operationName}")),
            HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Conflict
                => new ConnectionCodeClientException(
                    new SessionFailure(FailureCode.InvalidPayload, $"Connection code was rejected during {operationName}")),
            _ => new ConnectionCodeClientException(
                new SessionFailure(
                    FailureCode.PeerUnreachable,
                    $"Android peer failed during {operationName}: HTTP {(int)response.StatusCode} {body}".Trim()))
        };
    }

    private static ConnectionCodeClientException MapTransportFailure(Exception error)
    {
        if (error is TaskCanceledException or TimeoutException)
        {
            return new ConnectionCodeClientException(
                new SessionFailure(FailureCode.PeerUnreachable, "Timed out while contacting the Android peer"));
        }

        return new ConnectionCodeClientException(
            new SessionFailure(FailureCode.PeerUnreachable, error.Message));
    }
}
