package com.example.p2paudio.audio

internal class LateFrameRecoveryController(
    private val minSafeTrackPackets: Int = MIN_SAFE_TRACK_PACKETS,
    private val maxLateFrameWaitMs: Long = MAX_LATE_FRAME_WAIT_MS
) {
    private var awaitedSequence: Int? = null
    private var awaitedSinceRealtimeMs: Long = 0L

    fun reset() {
        awaitedSequence = null
        awaitedSinceRealtimeMs = 0L
    }

    fun shouldKeepWaiting(
        expectedSequence: Int,
        frameDurationMs: Long,
        queuedTrackFrames: Int,
        frameSamplesPerChannel: Int,
        nowRealtimeMs: Long
    ): Boolean {
        if (frameDurationMs <= 0L || queuedTrackFrames <= 0 || frameSamplesPerChannel <= 0) {
            return false
        }
        if (awaitedSequence != expectedSequence) {
            awaitedSequence = expectedSequence
            awaitedSinceRealtimeMs = nowRealtimeMs
        }

        val waitedMs = (nowRealtimeMs - awaitedSinceRealtimeMs).coerceAtLeast(0L)
        val queuedTrackPackets = ((queuedTrackFrames + frameSamplesPerChannel - 1) / frameSamplesPerChannel)
            .coerceAtLeast(0)
        val safeWaitPackets = (queuedTrackPackets - minSafeTrackPackets).coerceAtLeast(0)
        if (safeWaitPackets <= 0) {
            return false
        }

        val allowedWaitMs = (safeWaitPackets.toLong() * frameDurationMs)
            .coerceAtMost(maxLateFrameWaitMs)
        return waitedMs < allowedWaitMs
    }

    private companion object {
        private const val MIN_SAFE_TRACK_PACKETS = 2
        private const val MAX_LATE_FRAME_WAIT_MS = 200L
    }
}
