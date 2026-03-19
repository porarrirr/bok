package com.example.p2paudio.transport

import com.example.p2paudio.audio.UdpOpusPacket
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class UdpOpusListenerTransportQueueTest {

    @Test
    fun enqueueRealtimeDecodePacketDropsOldestPacketWhenQueueIsFull() {
        val pendingPackets = ArrayDeque<UdpOpusPacket>().apply {
            addLast(packet(sequence = 1))
            addLast(packet(sequence = 2))
        }
        val arrivalTimes = linkedMapOf(
            1 to 100L,
            2 to 120L
        )

        val droppedSequence = enqueueRealtimeDecodePacket(
            pendingPackets = pendingPackets,
            arrivalRealtimeMsBySequence = arrivalTimes,
            packet = packet(sequence = 3),
            arrivalRealtimeMs = 140L,
            maxQueuePackets = 2
        )

        assertEquals(1, droppedSequence)
        assertEquals(listOf(2, 3), pendingPackets.map { it.sequence })
        assertEquals(mapOf(2 to 120L, 3 to 140L), arrivalTimes)
    }

    @Test
    fun enqueueRealtimeDecodePacketKeepsAllPacketsWhenCapacityRemains() {
        val pendingPackets = ArrayDeque<UdpOpusPacket>()
        val arrivalTimes = mutableMapOf<Int, Long>()

        val droppedSequence = enqueueRealtimeDecodePacket(
            pendingPackets = pendingPackets,
            arrivalRealtimeMsBySequence = arrivalTimes,
            packet = packet(sequence = 9),
            arrivalRealtimeMs = 900L,
            maxQueuePackets = 2
        )

        assertNull(droppedSequence)
        assertEquals(listOf(9), pendingPackets.map { it.sequence })
        assertEquals(mapOf(9 to 900L), arrivalTimes)
    }

    private fun packet(sequence: Int): UdpOpusPacket {
        return UdpOpusPacket(
            sequence = sequence,
            timestampMs = sequence * 20L,
            sampleRate = 48_000,
            channels = 2,
            frameSamplesPerChannel = 960,
            opusPayload = byteArrayOf(1, 2, 3)
        )
    }
}
