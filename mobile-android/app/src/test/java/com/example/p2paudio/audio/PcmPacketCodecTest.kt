package com.example.p2paudio.audio

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

class PcmPacketCodecTest {

    @Test
    fun encodeDecodeRoundTrip_preservesFrame() {
        val frame = PcmFrame(
            sequence = 42,
            timestampMs = 1_760_000_123_456,
            sampleRate = 48_000,
            channels = 2,
            bitsPerSample = 16,
            frameSamplesPerChannel = 960,
            pcmBytes = ByteArray(3840) { (it % 127).toByte() }
        )

        val encoded = PcmPacketCodec.encode(frame)
        val decoded = PcmPacketCodec.decode(encoded)

        assertNotNull(decoded)
        decoded!!
        assertEquals(frame.sequence, decoded.sequence)
        assertEquals(frame.timestampMs, decoded.timestampMs)
        assertEquals(frame.sampleRate, decoded.sampleRate)
        assertEquals(frame.channels, decoded.channels)
        assertEquals(frame.bitsPerSample, decoded.bitsPerSample)
        assertEquals(frame.frameSamplesPerChannel, decoded.frameSamplesPerChannel)
        assertArrayEquals(frame.pcmBytes, decoded.pcmBytes)
    }

    @Test
    fun decodeRejectsUnsupportedVersion() {
        val frame = PcmFrame(
            sequence = 1,
            timestampMs = 1,
            sampleRate = 48_000,
            channels = 2,
            bitsPerSample = 16,
            frameSamplesPerChannel = 960,
            pcmBytes = ByteArray(10)
        )
        val encoded = PcmPacketCodec.encode(frame)
        encoded[0] = 99

        val decoded = PcmPacketCodec.decode(encoded)
        assertNull(decoded)
    }
}
