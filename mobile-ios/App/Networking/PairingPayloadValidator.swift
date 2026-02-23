import Foundation

enum PairingPayloadValidator {
    static func validateOffer(_ payload: SessionOfferPayload, nowUnixMs: Int64) throws {
        guard payload.version == "1", payload.role == "sender" else {
            throw SessionFailure(code: .invalidPayload, message: "Invalid offer version or role")
        }
        guard payload.expiresAtUnixMs >= nowUnixMs else {
            throw SessionFailure(code: .sessionExpired, message: "Offer expired")
        }
        guard !payload.sessionId.isEmpty, !payload.offerSdp.isEmpty else {
            throw SessionFailure(code: .invalidPayload, message: "Offer missing required fields")
        }
    }

    static func validateAnswer(
        _ payload: SessionAnswerPayload,
        expectedSessionId: String,
        nowUnixMs: Int64
    ) throws {
        guard payload.version == "1", payload.role == "receiver" else {
            throw SessionFailure(code: .invalidPayload, message: "Invalid answer version or role")
        }
        guard payload.expiresAtUnixMs >= nowUnixMs else {
            throw SessionFailure(code: .sessionExpired, message: "Answer expired")
        }
        guard payload.sessionId == expectedSessionId else {
            throw SessionFailure(code: .invalidPayload, message: "Session ID mismatch")
        }
        guard !payload.answerSdp.isEmpty else {
            throw SessionFailure(code: .invalidPayload, message: "Answer SDP is empty")
        }
    }
}
