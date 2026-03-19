package com.example.p2paudio.audio

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.util.PriorityQueue

class AndroidPcmPlayerQueueOverflowTest {

    @Test
    fun trimOverflowFramesResyncsExpectedSequencePastDroppedFrames() {
        val pendingFrames = PriorityQueue<PcmFrame>(compareBy { it.sequence }).apply {
            add(frame(sequence = 100))
            add(frame(sequence = 101))
            add(frame(sequence = 102))
            add(frame(sequence = 103))
            add(frame(sequence = 104))
        }

        val result = trimOverflowFramesForRealtimePlayback(
            pendingFrames = pendingFrames,
            maxQueueFrames = 3,
            expectedSequence = 100
        )

        assertEquals(2, result.droppedFrameCount)
        assertEquals(100, result.firstDroppedSequence)
        assertEquals(101, result.lastDroppedSequence)
        assertEquals(102, result.nextExpectedSequence)
        assertEquals(listOf(102, 103, 104), pendingFrames.toList().map { it.sequence }.sorted())
    }

    @Test
    fun trimOverflowFramesLeavesExpectedSequenceUnsetBeforePlaybackStarts() {
        val pendingFrames = PriorityQueue<PcmFrame>(compareBy { it.sequence }).apply {
            add(frame(sequence = 8))
            add(frame(sequence = 9))
            add(frame(sequence = 10))
            add(frame(sequence = 11))
        }

        val result = trimOverflowFramesForRealtimePlayback(
            pendingFrames = pendingFrames,
            maxQueueFrames = 2,
            expectedSequence = null
        )

        assertEquals(2, result.droppedFrameCount)
        assertNull(result.nextExpectedSequence)
        assertEquals(listOf(10, 11), pendingFrames.toList().map { it.sequence }.sorted())
    }

    private fun frame(sequence: Int): PcmFrame {
        return PcmFrame(
            sequence = sequence,
            timestampMs = sequence * 20L,
            sampleRate = 48_000,
            channels = 2,
            bitsPerSample = 16,
            frameSamplesPerChannel = 960,
            pcmBytes = ByteArray(3_840)
        )
    }
}
