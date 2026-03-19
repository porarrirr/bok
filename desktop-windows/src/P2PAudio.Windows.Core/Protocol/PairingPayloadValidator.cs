using P2PAudio.Windows.Core.Models;

namespace P2PAudio.Windows.Core.Protocol;

public static class PairingPayloadValidator
{
    public static SessionFailure? ValidateInit(PairingInitPayload payload, long nowUnixMs)
    {
        if (payload.Version != "2" || payload.Phase != "init")
        {
            return new SessionFailure(FailureCode.InvalidPayload, "Invalid init payload version/phase");
        }
        if (payload.ExpiresAtUnixMs < nowUnixMs)
        {
            return new SessionFailure(FailureCode.SessionExpired, "Init payload expired");
        }
        if (string.IsNullOrWhiteSpace(payload.SessionId) ||
            string.IsNullOrWhiteSpace(payload.SenderPubKeyFingerprint) ||
            string.IsNullOrWhiteSpace(payload.OfferSdp))
        {
            return new SessionFailure(FailureCode.InvalidPayload, "Init payload missing required fields");
        }
        return null;
    }

    public static SessionFailure? ValidateConfirm(
        PairingConfirmPayload payload,
        string expectedSessionId,
        long nowUnixMs
    )
    {
        if (payload.Version != "2" || payload.Phase != "confirm")
        {
            return new SessionFailure(FailureCode.InvalidPayload, "Invalid confirm payload version/phase");
        }
        if (payload.ExpiresAtUnixMs < nowUnixMs)
        {
            return new SessionFailure(FailureCode.SessionExpired, "Confirm payload expired");
        }
        if (!string.Equals(payload.SessionId, expectedSessionId, StringComparison.Ordinal))
        {
            return new SessionFailure(FailureCode.InvalidPayload, "Session ID does not match");
        }
        if (string.IsNullOrWhiteSpace(payload.ReceiverPubKeyFingerprint) ||
            string.IsNullOrWhiteSpace(payload.AnswerSdp))
        {
            return new SessionFailure(FailureCode.InvalidPayload, "Confirm payload missing required fields");
        }
        return null;
    }

    public static SessionFailure? ValidateUdpInit(UdpInitPayload payload, long nowUnixMs)
    {
        if (payload.Version != "2" ||
            payload.Phase != "udp_init" ||
            payload.Transport != "udp_opus")
        {
            return new SessionFailure(FailureCode.InvalidPayload, "Invalid UDP init payload version/phase");
        }
        if (payload.ExpiresAtUnixMs < nowUnixMs)
        {
            return new SessionFailure(FailureCode.SessionExpired, "UDP init payload expired");
        }
        if (string.IsNullOrWhiteSpace(payload.SessionId) ||
            string.IsNullOrWhiteSpace(payload.SenderDeviceName))
        {
            return new SessionFailure(FailureCode.InvalidPayload, "UDP init payload missing required fields");
        }
        return null;
    }

    public static SessionFailure? ValidateUdpConfirm(
        UdpConfirmPayload payload,
        string expectedSessionId,
        long nowUnixMs)
    {
        if (payload.Version != "2" ||
            payload.Phase != "udp_confirm" ||
            payload.Transport != "udp_opus")
        {
            return new SessionFailure(FailureCode.InvalidPayload, "Invalid UDP confirm payload version/phase");
        }
        if (payload.ExpiresAtUnixMs < nowUnixMs)
        {
            return new SessionFailure(FailureCode.SessionExpired, "UDP confirm payload expired");
        }
        if (!string.Equals(payload.SessionId, expectedSessionId, StringComparison.Ordinal))
        {
            return new SessionFailure(FailureCode.InvalidPayload, "Session ID does not match");
        }
        if (string.IsNullOrWhiteSpace(payload.ReceiverDeviceName) ||
            payload.ReceiverPort <= 0 ||
            payload.ReceiverPort > ushort.MaxValue)
        {
            return new SessionFailure(FailureCode.InvalidPayload, "UDP confirm payload missing required fields");
        }
        return null;
    }
}
