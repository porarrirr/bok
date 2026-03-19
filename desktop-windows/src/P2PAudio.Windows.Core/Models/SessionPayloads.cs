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

public sealed record UdpInitPayload(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("transport")] string Transport,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("senderDeviceName")] string SenderDeviceName,
    [property: JsonPropertyName("expiresAtUnixMs")] long ExpiresAtUnixMs
)
{
    public static UdpInitPayload Create(
        string sessionId,
        string senderDeviceName,
        long expiresAtUnixMs
    ) => new(
        Version: "2",
        Phase: "udp_init",
        Transport: "udp_opus",
        SessionId: sessionId,
        SenderDeviceName: senderDeviceName,
        ExpiresAtUnixMs: expiresAtUnixMs
    );
}

public sealed record UdpConfirmPayload(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("transport")] string Transport,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("receiverDeviceName")] string ReceiverDeviceName,
    [property: JsonPropertyName("receiverPort")] int ReceiverPort,
    [property: JsonPropertyName("expiresAtUnixMs")] long ExpiresAtUnixMs
)
{
    public static UdpConfirmPayload Create(
        string sessionId,
        string receiverDeviceName,
        int receiverPort,
        long expiresAtUnixMs
    ) => new(
        Version: "2",
        Phase: "udp_confirm",
        Transport: "udp_opus",
        SessionId: sessionId,
        ReceiverDeviceName: receiverDeviceName,
        ReceiverPort: receiverPort,
        ExpiresAtUnixMs: expiresAtUnixMs
    );
}
