package com.example.p2paudio.protocol

import com.example.p2paudio.model.FailureCode
import com.example.p2paudio.model.SessionAnswerPayload
import com.example.p2paudio.model.SessionFailure
import com.example.p2paudio.model.SessionOfferPayload

object PairingPayloadValidator {

    fun validateOffer(payload: SessionOfferPayload, nowUnixMs: Long): SessionFailure? {
        if (payload.version != "1" || payload.role != "sender") {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Invalid offer version/role")
        }
        if (payload.expiresAtUnixMs < nowUnixMs) {
            return SessionFailure(FailureCode.SESSION_EXPIRED, "Offer payload expired")
        }
        if (payload.sessionId.isBlank() || payload.offerSdp.isBlank()) {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Offer payload missing required fields")
        }
        return null
    }

    fun validateAnswer(
        payload: SessionAnswerPayload,
        expectedSessionId: String,
        nowUnixMs: Long
    ): SessionFailure? {
        if (payload.version != "1" || payload.role != "receiver") {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Invalid answer version/role")
        }
        if (payload.expiresAtUnixMs < nowUnixMs) {
            return SessionFailure(FailureCode.SESSION_EXPIRED, "Answer payload expired")
        }
        if (payload.sessionId != expectedSessionId) {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Session ID does not match")
        }
        if (payload.answerSdp.isBlank()) {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Answer SDP is empty")
        }
        return null
    }
}
