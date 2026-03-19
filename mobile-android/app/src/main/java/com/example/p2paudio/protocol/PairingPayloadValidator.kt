package com.example.p2paudio.protocol

import com.example.p2paudio.model.FailureCode
import com.example.p2paudio.model.PairingConfirmPayload
import com.example.p2paudio.model.PairingInitPayload
import com.example.p2paudio.model.SessionFailure
import com.example.p2paudio.model.UdpConfirmPayload
import com.example.p2paudio.model.UdpInitPayload

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

    fun validateUdpInit(payload: UdpInitPayload, nowUnixMs: Long): SessionFailure? {
        if (payload.version != "2" ||
            payload.phase != "udp_init" ||
            payload.transport != "udp_opus"
        ) {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Invalid UDP init payload version/phase")
        }
        if (payload.expiresAtUnixMs < nowUnixMs) {
            return SessionFailure(FailureCode.SESSION_EXPIRED, "UDP init payload expired")
        }
        if (payload.sessionId.isBlank() || payload.senderDeviceName.isBlank()) {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "UDP init payload missing required fields")
        }
        return null
    }

    fun validateUdpConfirm(
        payload: UdpConfirmPayload,
        expectedSessionId: String,
        nowUnixMs: Long
    ): SessionFailure? {
        if (payload.version != "2" ||
            payload.phase != "udp_confirm" ||
            payload.transport != "udp_opus"
        ) {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Invalid UDP confirm payload version/phase")
        }
        if (payload.expiresAtUnixMs < nowUnixMs) {
            return SessionFailure(FailureCode.SESSION_EXPIRED, "UDP confirm payload expired")
        }
        if (payload.sessionId != expectedSessionId) {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "Session ID does not match")
        }
        if (payload.receiverDeviceName.isBlank() ||
            payload.receiverPort <= 0 ||
            payload.receiverPort > 65_535
        ) {
            return SessionFailure(FailureCode.INVALID_PAYLOAD, "UDP confirm payload missing required fields")
        }
        return null
    }
}
