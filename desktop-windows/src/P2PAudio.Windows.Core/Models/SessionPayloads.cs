using System.Text.Json.Serialization;

namespace P2PAudio.Windows.Core.Models;

public sealed record PairingInitPayload(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("senderDeviceName")] string SenderDeviceName,
    [property: JsonPropertyName("senderPubKeyFingerprint")] string SenderPubKeyFingerprint,
    [property: JsonPropertyName("offerSdp")] string OfferSdp,
    [property: JsonPropertyName("expiresAtUnixMs")] long ExpiresAtUnixMs
)
{
    public static PairingInitPayload Create(
        string sessionId,
        string senderDeviceName,
        string senderPubKeyFingerprint,
        string offerSdp,
        long expiresAtUnixMs
    ) => new(
        Version: "2",
        Phase: "init",
        SessionId: sessionId,
        SenderDeviceName: senderDeviceName,
        SenderPubKeyFingerprint: senderPubKeyFingerprint,
        OfferSdp: offerSdp,
        ExpiresAtUnixMs: expiresAtUnixMs
    );
}

public sealed record PairingConfirmPayload(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("receiverDeviceName")] string ReceiverDeviceName,
    [property: JsonPropertyName("receiverPubKeyFingerprint")] string ReceiverPubKeyFingerprint,
    [property: JsonPropertyName("answerSdp")] string AnswerSdp,
    [property: JsonPropertyName("expiresAtUnixMs")] long ExpiresAtUnixMs
)
{
    public static PairingConfirmPayload Create(
        string sessionId,
        string receiverDeviceName,
        string receiverPubKeyFingerprint,
        string answerSdp,
        long expiresAtUnixMs
    ) => new(
        Version: "2",
        Phase: "confirm",
        SessionId: sessionId,
        ReceiverDeviceName: receiverDeviceName,
        ReceiverPubKeyFingerprint: receiverPubKeyFingerprint,
        AnswerSdp: answerSdp,
        ExpiresAtUnixMs: expiresAtUnixMs
    );
}
