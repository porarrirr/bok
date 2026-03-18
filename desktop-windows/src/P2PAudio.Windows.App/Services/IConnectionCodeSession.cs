namespace P2PAudio.Windows.App.Services;

public interface IConnectionCodeSession : IDisposable
{
    string ConnectionCode { get; }

    long ExpiresAtUnixMs { get; }

    Task<string> WaitForConfirmPayloadAsync(CancellationToken cancellationToken);
}

public interface IConnectionCodeSessionFactory
{
    IConnectionCodeSession Create(string initPayload, string offerSdp, long expiresAtUnixMs);
}
