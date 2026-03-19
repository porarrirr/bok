namespace P2PAudio.Windows.App.Services;

public interface IConnectionCodeSession : IDisposable
{
    string ConnectionCode { get; }

    long ExpiresAtUnixMs { get; }

    Task<ConnectionCodeSubmission> WaitForConfirmPayloadAsync(CancellationToken cancellationToken);
}

public interface IConnectionCodeSessionFactory
{
    IConnectionCodeSession Create(string initPayload, string localAddressHintSource, long expiresAtUnixMs);
}

public sealed record ConnectionCodeSubmission(
    string Payload,
    string RemoteAddress
);
