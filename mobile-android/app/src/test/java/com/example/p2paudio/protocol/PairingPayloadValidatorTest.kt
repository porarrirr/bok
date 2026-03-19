package com.example.p2paudio.protocol

import com.example.p2paudio.model.FailureCode
import com.example.p2paudio.model.UdpConfirmPayload
import com.example.p2paudio.model.UdpInitPayload
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test

class PairingPayloadValidatorTest {

    @Test
    fun `validateUdpInit returns session expired for expired payload`() {
        val now = 1_760_000_000_000L
        val payload = UdpInitPayload(
            sessionId = "udp-session-1",
            senderDeviceName = "windows",
            expiresAtUnixMs = now - 1
        )

        val failure = PairingPayloadValidator.validateUdpInit(payload, now)

        assertNotNull(failure)
        assertEquals(FailureCode.SESSION_EXPIRED, failure?.code)
    }

    @Test
    fun `validateUdpConfirm returns invalid payload for session mismatch`() {
        val now = 1_760_000_000_000L
        val payload = UdpConfirmPayload(
            sessionId = "udp-session-a",
            receiverDeviceName = "pixel",
            receiverPort = 49_152,
            expiresAtUnixMs = now + 60_000
        )

        val failure = PairingPayloadValidator.validateUdpConfirm(
            payload = payload,
            expectedSessionId = "udp-session-b",
            nowUnixMs = now
        )

        assertNotNull(failure)
        assertEquals(FailureCode.INVALID_PAYLOAD, failure?.code)
    }
}
