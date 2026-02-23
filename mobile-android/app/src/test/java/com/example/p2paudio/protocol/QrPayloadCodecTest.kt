package com.example.p2paudio.protocol

import com.example.p2paudio.model.SessionAnswerPayload
import com.example.p2paudio.model.SessionOfferPayload
import java.io.ByteArrayOutputStream
import java.util.Base64
import java.util.zip.Deflater
import java.util.zip.DeflaterOutputStream
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class QrPayloadCodecTest {

    @Test
    fun `encodeOffer compresses and decodes large payload`() {
        val payload = SessionOfferPayload(
            sessionId = "session-1",
            senderDeviceName = "pixel",
            senderPubKeyFingerprint = "fp",
            offerSdp = "v=0\n" + "a=candidate:1 1 UDP 12345 192.168.0.10 5000 typ host\n".repeat(120),
            expiresAtUnixMs = 1_760_000_000_000L
        )

        val encoded = QrPayloadCodec.encodeOffer(payload)
        assertTrue(encoded.startsWith("p2paudio-z1:"))

        val decoded = QrPayloadCodec.decodeOffer(encoded)
        assertEquals(payload, decoded)
    }

    @Test
    fun `decodeOffer supports legacy raw json`() {
        val payload = SessionOfferPayload(
            sessionId = "session-legacy",
            senderDeviceName = "pixel",
            senderPubKeyFingerprint = "fp",
            offerSdp = "v=0\na=fingerprint:sha-256 test\n",
            expiresAtUnixMs = 1_760_000_000_000L
        )

        val rawJson = """
            {
              "version":"1",
              "role":"sender",
              "sessionId":"${payload.sessionId}",
              "senderDeviceName":"${payload.senderDeviceName}",
              "senderPubKeyFingerprint":"${payload.senderPubKeyFingerprint}",
              "offerSdp":"${payload.offerSdp.replace("\n", "\\n")}",
              "expiresAtUnixMs":${payload.expiresAtUnixMs}
            }
        """.trimIndent()

        val decoded = QrPayloadCodec.decodeOffer(rawJson)
        assertEquals(payload, decoded)
    }

    @Test
    fun `encodeAnswer keeps short payload as plain json`() {
        val payload = SessionAnswerPayload(
            sessionId = "session-2",
            receiverDeviceName = "iphone",
            receiverPubKeyFingerprint = "fp",
            answerSdp = "v=0\na=fingerprint:sha-256 test\n",
            expiresAtUnixMs = 1_760_000_000_000L
        )

        val encoded = QrPayloadCodec.encodeAnswer(payload)
        assertFalse(encoded.startsWith("p2paudio-z1:"))

        val decoded = QrPayloadCodec.decodeAnswer(encoded)
        assertEquals(payload, decoded)
    }

    @Test
    fun `decodeOffer rejects empty compressed payload`() {
        assertThrows(IllegalArgumentException::class.java) {
            QrPayloadCodec.decodeOffer("p2paudio-z1:")
        }
    }

    @Test
    fun `decodeOffer rejects invalid compressed base64`() {
        assertThrows(IllegalArgumentException::class.java) {
            QrPayloadCodec.decodeOffer("p2paudio-z1:***invalid***")
        }
    }

    @Test
    fun `decodeOffer rejects compressed payload that exceeds decompressed size limit`() {
        val oversizedPlain = ByteArray(530_000) { 'a'.code.toByte() }
        val compressed = zlibCompress(oversizedPlain)
        val encoded = Base64.getUrlEncoder().withoutPadding().encodeToString(compressed)

        assertThrows(IllegalArgumentException::class.java) {
            QrPayloadCodec.decodeOffer("p2paudio-z1:$encoded")
        }
    }

    private fun zlibCompress(input: ByteArray): ByteArray {
        val output = ByteArrayOutputStream(input.size / 4)
        DeflaterOutputStream(output, Deflater(Deflater.BEST_SPEED)).use { stream ->
            stream.write(input)
        }
        return output.toByteArray()
    }
}
