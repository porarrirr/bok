package com.example.p2paudio.protocol

import com.example.p2paudio.model.PairingConfirmPayload
import com.example.p2paudio.model.PairingInitPayload
import com.example.p2paudio.model.UdpConfirmPayload
import com.example.p2paudio.model.UdpInitPayload
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
    fun `encodeInit compresses and decodes large payload`() {
        val payload = PairingInitPayload(
            sessionId = "session-1",
            senderDeviceName = "pixel",
            senderPubKeyFingerprint = "fp",
            offerSdp = "v=0\n" + "a=candidate:1 1 UDP 12345 192.168.0.10 5000 typ host\n".repeat(120),
            expiresAtUnixMs = 1_760_000_000_000L
        )

        val encoded = QrPayloadCodec.encodeInit(payload)
        assertTrue(encoded.startsWith("p2paudio-z1:"))

        val decoded = QrPayloadCodec.decodeInit(encoded)
        assertEquals(payload, decoded)
    }

    @Test
    fun `decodeInit rejects legacy v1 raw json`() {
        val rawJson = """
            {
              "version":"1",
              "role":"sender",
              "sessionId":"session-legacy",
              "senderDeviceName":"pixel",
              "senderPubKeyFingerprint":"fp",
              "offerSdp":"v=0\\na=fingerprint:sha-256 test\\n",
              "expiresAtUnixMs":1760000000000
            }
        """.trimIndent()

        assertThrows(Exception::class.java) {
            QrPayloadCodec.decodeInit(rawJson)
        }
    }

    @Test
    fun `encodeConfirm keeps short payload as plain json`() {
        val payload = PairingConfirmPayload(
            sessionId = "session-2",
            receiverDeviceName = "iphone",
            receiverPubKeyFingerprint = "fp",
            answerSdp = "v=0\na=fingerprint:sha-256 test\n",
            expiresAtUnixMs = 1_760_000_000_000L
        )

        val encoded = QrPayloadCodec.encodeConfirm(payload)
        assertFalse(encoded.startsWith("p2paudio-z1:"))

        val decoded = QrPayloadCodec.decodeConfirm(encoded)
        assertEquals(payload, decoded)
    }

    @Test
    fun `decodeInit rejects empty compressed payload`() {
        assertThrows(IllegalArgumentException::class.java) {
            QrPayloadCodec.decodeInit("p2paudio-z1:")
        }
    }

    @Test
    fun `decodeInit rejects invalid compressed base64`() {
        assertThrows(IllegalArgumentException::class.java) {
            QrPayloadCodec.decodeInit("p2paudio-z1:***invalid***")
        }
    }

    @Test
    fun `decodeInit rejects compressed payload that exceeds decompressed size limit`() {
        val oversizedPlain = ByteArray(530_000) { 'a'.code.toByte() }
        val compressed = zlibCompress(oversizedPlain)
        val encoded = Base64.getUrlEncoder().withoutPadding().encodeToString(compressed)

        assertThrows(IllegalArgumentException::class.java) {
            QrPayloadCodec.decodeInit("p2paudio-z1:$encoded")
        }
    }

    @Test
    fun `encodeUdpInit and decodeUdpInit round trip`() {
        val payload = UdpInitPayload(
            sessionId = "udp-session-1",
            senderDeviceName = "windows",
            expiresAtUnixMs = 1_760_000_000_000L
        )

        val encoded = QrPayloadCodec.encodeUdpInit(payload)
        val decoded = QrPayloadCodec.decodeUdpInit(encoded)

        assertEquals(payload, decoded)
    }

    @Test
    fun `encodeUdpConfirm and decodeUdpConfirm round trip`() {
        val payload = UdpConfirmPayload(
            sessionId = "udp-session-1",
            receiverDeviceName = "pixel",
            receiverPort = 49_152,
            expiresAtUnixMs = 1_760_000_000_000L
        )

        val encoded = QrPayloadCodec.encodeUdpConfirm(payload)
        val decoded = QrPayloadCodec.decodeUdpConfirm(encoded)

        assertEquals(payload, decoded)
    }

    private fun zlibCompress(input: ByteArray): ByteArray {
        val output = ByteArrayOutputStream(input.size / 4)
        DeflaterOutputStream(output, Deflater(Deflater.BEST_SPEED)).use { stream ->
            stream.write(input)
        }
        return output.toByteArray()
    }
}
