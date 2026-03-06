using P2PAudio.Windows.Core.Models;
using P2PAudio.Windows.Core.Protocol;

namespace P2PAudio.Windows.Core.Tests;

public sealed class QrPayloadCodecTests
{
    [Fact]
    public void EncodeDecodeInit_RoundTrip()
    {
        var payload = PairingInitPayload.Create(
            sessionId: Guid.NewGuid().ToString(),
            senderDeviceName: "windows",
            senderPubKeyFingerprint: "sha-256-fp",
            offerSdp: "v=0\r\ns=offer\r\n",
            expiresAtUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000
        );

        var encoded = QrPayloadCodec.EncodeInit(payload);
        var decoded = QrPayloadCodec.DecodeInit(encoded);

        Assert.Equal("2", decoded.Version);
        Assert.Equal("init", decoded.Phase);
        Assert.Equal(payload.SessionId, decoded.SessionId);
        Assert.Equal(payload.OfferSdp, decoded.OfferSdp);
    }

    [Fact]
    public void DecodeConfirm_InvalidPayload_Throws()
    {
        Assert.Throws<SessionFailure>(() => QrPayloadCodec.DecodeConfirm("p2paudio-z1:invalid"));
    }
}
