using P2PAudio.Windows.Core.Models;
using P2PAudio.Windows.Core.Protocol;

namespace P2PAudio.Windows.Core.Tests;

public sealed class PairingPayloadValidatorTests
{
    [Fact]
    public void ValidateInit_Expired_ReturnsSessionExpired()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = PairingInitPayload.Create(
            sessionId: Guid.NewGuid().ToString(),
            senderDeviceName: "windows",
            senderPubKeyFingerprint: "fp",
            offerSdp: "v=0\r\n",
            expiresAtUnixMs: now - 1
        );

        var failure = PairingPayloadValidator.ValidateInit(payload, now);

        Assert.NotNull(failure);
        Assert.Equal(FailureCode.SessionExpired, failure!.Code);
    }

    [Fact]
    public void ValidateConfirm_SessionMismatch_ReturnsInvalidPayload()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = PairingConfirmPayload.Create(
            sessionId: "session-a",
            receiverDeviceName: "windows",
            receiverPubKeyFingerprint: "fp",
            answerSdp: "v=0\r\n",
            expiresAtUnixMs: now + 60_000
        );

        var failure = PairingPayloadValidator.ValidateConfirm(payload, expectedSessionId: "session-b", nowUnixMs: now);

        Assert.NotNull(failure);
        Assert.Equal(FailureCode.InvalidPayload, failure!.Code);
    }

    [Fact]
    public void ValidateInit_MissingSenderFingerprint_ReturnsInvalidPayload()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = PairingInitPayload.Create(
            sessionId: Guid.NewGuid().ToString(),
            senderDeviceName: "windows",
            senderPubKeyFingerprint: string.Empty,
            offerSdp: "v=0\r\n",
            expiresAtUnixMs: now + 60_000
        );

        var failure = PairingPayloadValidator.ValidateInit(payload, now);

        Assert.NotNull(failure);
        Assert.Equal(FailureCode.InvalidPayload, failure!.Code);
    }

    [Fact]
    public void ValidateConfirm_MissingReceiverFingerprint_ReturnsInvalidPayload()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = PairingConfirmPayload.Create(
            sessionId: "session-a",
            receiverDeviceName: "windows",
            receiverPubKeyFingerprint: string.Empty,
            answerSdp: "v=0\r\n",
            expiresAtUnixMs: now + 60_000
        );

        var failure = PairingPayloadValidator.ValidateConfirm(payload, expectedSessionId: "session-a", nowUnixMs: now);

        Assert.NotNull(failure);
        Assert.Equal(FailureCode.InvalidPayload, failure!.Code);
    }
}
