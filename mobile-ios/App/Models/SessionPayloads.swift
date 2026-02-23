import Foundation

struct SessionOfferPayload: Codable {
    let version: String
    let role: String
    let sessionId: String
    let senderDeviceName: String
    let senderPubKeyFingerprint: String
    let offerSdp: String
    let expiresAtUnixMs: Int64

    init(
        sessionId: String,
        senderDeviceName: String,
        senderPubKeyFingerprint: String,
        offerSdp: String,
        expiresAtUnixMs: Int64
    ) {
        self.version = "1"
        self.role = "sender"
        self.sessionId = sessionId
        self.senderDeviceName = senderDeviceName
        self.senderPubKeyFingerprint = senderPubKeyFingerprint
        self.offerSdp = offerSdp
        self.expiresAtUnixMs = expiresAtUnixMs
    }
}

struct SessionAnswerPayload: Codable {
    let version: String
    let role: String
    let sessionId: String
    let receiverDeviceName: String
    let receiverPubKeyFingerprint: String
    let answerSdp: String
    let expiresAtUnixMs: Int64

    init(
        sessionId: String,
        receiverDeviceName: String,
        receiverPubKeyFingerprint: String,
        answerSdp: String,
        expiresAtUnixMs: Int64
    ) {
        self.version = "1"
        self.role = "receiver"
        self.sessionId = sessionId
        self.receiverDeviceName = receiverDeviceName
        self.receiverPubKeyFingerprint = receiverPubKeyFingerprint
        self.answerSdp = answerSdp
        self.expiresAtUnixMs = expiresAtUnixMs
    }
}
