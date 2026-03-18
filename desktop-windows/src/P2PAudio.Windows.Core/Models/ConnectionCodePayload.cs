namespace P2PAudio.Windows.Core.Models;

public sealed record ConnectionCodePayload(
    string Host,
    int Port,
    string Token,
    long ExpiresAtUnixMs
);
