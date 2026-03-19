import Foundation

enum PairingPayloadValidator {
    static func validateInit(_ payload: PairingInitPayload, nowUnixMs: Int64) throws {
        guard payload.version == "2", payload.phase == "init" else {
            throw SessionFailure(code: .invalidPayload, message: L10n.tr("error.invalid_init_payload"))
        }
        guard payload.expiresAtUnixMs >= nowUnixMs else {
            throw SessionFailure(code: .sessionExpired, message: L10n.tr("error.session_expired"))
        }
        guard !payload.sessionId.isEmpty, !payload.offerSdp.isEmpty else {
            throw SessionFailure(code: .invalidPayload, message: L10n.tr("error.invalid_init_payload"))
        }
    }

    static func validateConfirm(
        _ payload: PairingConfirmPayload,
        expectedSessionId: String,
        nowUnixMs: Int64
    ) throws {
        guard payload.version == "2", payload.phase == "confirm" else {
            throw SessionFailure(code: .invalidPayload, message: L10n.tr("error.invalid_confirm_payload"))
        }
        guard payload.expiresAtUnixMs >= nowUnixMs else {
            throw SessionFailure(code: .sessionExpired, message: L10n.tr("error.session_expired"))
        }
        guard payload.sessionId == expectedSessionId else {
            throw SessionFailure(code: .invalidPayload, message: L10n.tr("error.invalid_confirm_payload"))
        }
        guard !payload.answerSdp.isEmpty else {
            throw SessionFailure(code: .invalidPayload, message: L10n.tr("error.invalid_confirm_payload"))
        }
    }
}
