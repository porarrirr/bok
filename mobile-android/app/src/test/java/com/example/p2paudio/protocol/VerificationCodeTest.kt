package com.example.p2paudio.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class VerificationCodeTest {

    @Test
    fun `matches the shared cross-platform verification vector`() {
        val code = VerificationCode.fromSessionAndFingerprints(
            sessionId = "session-a",
            senderFingerprint = "sender-fp",
            receiverFingerprint = "receiver-fp"
        )

        assertEquals("912851", code)
    }

    @Test
    fun `formats a six digit verification code`() {
        val code = VerificationCode.fromSessionAndFingerprints(
            sessionId = "session-1",
            senderFingerprint = "sender-fp",
            receiverFingerprint = "receiver-fp"
        )

        assertEquals(6, code.length)
        assertTrue(code.all(Char::isDigit))
    }
}
