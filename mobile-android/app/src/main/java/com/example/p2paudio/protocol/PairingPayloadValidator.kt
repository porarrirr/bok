package com.example.p2paudio.protocol

import com.example.p2paudio.model.FailureCode
import com.example.p2paudio.model.PairingConfirmPayload
import com.example.p2paudio.model.PairingInitPayload
import com.example.p2paudio.model.SessionFailure

object PairingPayloadValidator {

    fun validateInit(payload: PairingInitPayload, nowUnixMs: Long): SessionFailure? {
        if (payload.version != "2" || payload.phase != "init") {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Invalid init payload version/phase")
        }
        if (payload.expiresAtUnixMs < nowUnixMs) {
            return SessionFailure(FailureCode.SESSION_EXPIRED, "Init payload expired")
        }
        if (payload.sessionId.isBlank() || payload.offerSdp.isBlank()) {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Init payload missing required fields")
        }
        return null
    }

    fun validateConfirm(
        payload: PairingConfirmPayload,
        expectedSessionId: String,
        nowUnixMs: Long
    ): SessionFailure? {
        if (payload.version != "2" || payload.phase != "confirm") {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Invalid confirm payload version/phase")
        }
        if (payload.expiresAtUnixMs < nowUnixMs) {
            return SessionFailure(FailureCode.SESSION_EXPIRED, "Confirm payload expired")
        }
        if (payload.sessionId != expectedSessionId) {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Session ID does not match")
        }
        if (payload.answerSdp.isBlank()) {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Confirm SDP is empty")
        }
        return null
    }
}
