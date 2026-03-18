package com.example.p2paudio.protocol

import com.example.p2paudio.model.ConnectionCodePayload
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.assertThrows
import org.junit.Test

class ConnectionCodeCodecTest {

    @Test
    fun `encode and decode round trip`() {
        val payload = ConnectionCodePayload(
            host = "192.168.137.1",
            port = 45678,
            token = "token-123",
            expiresAtUnixMs = 1_760_000_000_000L
        )

        val encoded = ConnectionCodeCodec.encode(payload)
        val decoded = ConnectionCodeCodec.decode(encoded)

        assertEquals(payload, decoded)
    }

    @Test
    fun `looksLikeConnectionCode matches new prefix`() {
        assertTrue(ConnectionCodeCodec.looksLikeConnectionCode("p2paudio-c1:192.168.0.10:45678:1:token"))
        assertFalse(ConnectionCodeCodec.looksLikeConnectionCode("p2paudio-z1:payload"))
    }

    @Test
    fun `decode rejects invalid prefix`() {
        assertThrows(IllegalArgumentException::class.java) {
            ConnectionCodeCodec.decode("invalid-code")
        }
    }
}
