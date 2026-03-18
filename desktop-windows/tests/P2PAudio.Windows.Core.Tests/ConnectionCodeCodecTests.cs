using P2PAudio.Windows.Core.Models;
using P2PAudio.Windows.Core.Protocol;

namespace P2PAudio.Windows.Core.Tests;

public sealed class ConnectionCodeCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTrip()
    {
        var payload = new ConnectionCodePayload(
            Host: "192.168.137.1",
            Port: 45678,
            Token: "token-123",
            ExpiresAtUnixMs: 1_760_000_000_000
        );

        var encoded = ConnectionCodeCodec.Encode(payload);
        var decoded = ConnectionCodeCodec.Decode(encoded);

        Assert.Equal(payload, decoded);
    }

    [Fact]
    public void Decode_InvalidPrefix_Throws()
    {
        Assert.Throws<ArgumentException>(() => ConnectionCodeCodec.Decode("invalid-code"));
    }

    [Fact]
    public void LooksLikeConnectionCode_MatchesPrefix()
    {
        Assert.True(ConnectionCodeCodec.LooksLikeConnectionCode("p2paudio-c1:192.168.0.10:45678:1:token"));
        Assert.False(ConnectionCodeCodec.LooksLikeConnectionCode("p2paudio-z1:payload"));
    }
}
