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

    [Fact]
    public void EncodeDecodeUdpInit_RoundTrip()
    {
        var payload = UdpInitPayload.Create(
            sessionId: Guid.NewGuid().ToString(),
            senderDeviceName: "windows",
            expiresAtUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000
        );

        var encoded = QrPayloadCodec.EncodeUdpInit(payload);
        var decoded = QrPayloadCodec.DecodeUdpInit(encoded);

        Assert.Equal("2", decoded.Version);
        Assert.Equal("udp_init", decoded.Phase);
        Assert.Equal("udp_opus", decoded.Transport);
        Assert.Equal(payload.SessionId, decoded.SessionId);
    }

    [Fact]
    public void EncodeDecodeUdpConfirm_RoundTrip()
    {
        var payload = UdpConfirmPayload.Create(
            sessionId: Guid.NewGuid().ToString(),
            receiverDeviceName: "android",
            receiverPort: 49_152,
            expiresAtUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000
        );

        var encoded = QrPayloadCodec.EncodeUdpConfirm(payload);
        var decoded = QrPayloadCodec.DecodeUdpConfirm(encoded);

        Assert.Equal("2", decoded.Version);
        Assert.Equal("udp_confirm", decoded.Phase);
        Assert.Equal("udp_opus", decoded.Transport);
        Assert.Equal(payload.ReceiverPort, decoded.ReceiverPort);
    }
}
