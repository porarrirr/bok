namespace P2PAudio.Windows.App.Services;

public interface IUdpReceiverDiscoveryService
{
    Task<IReadOnlyList<UdpReceiverEndpoint>> DiscoverAsync(CancellationToken cancellationToken);
}

public sealed record UdpReceiverEndpoint(
    string DisplayName,
    string ServiceName,
    string Host,
    int Port
);
