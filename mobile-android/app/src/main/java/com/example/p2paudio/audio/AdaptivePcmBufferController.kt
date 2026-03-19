package com.example.p2paudio.audio

import kotlin.math.abs
import kotlin.math.ceil
import kotlin.math.max
import kotlin.math.roundToInt

internal data class AdaptivePcmBufferSnapshot(
    val startupTargetFrames: Int,
    val targetPrebufferFrames: Int,
    val basePrebufferFrames: Int,
    val estimatedJitterMs: Int
)

internal class AdaptivePcmBufferController(
    private val startupPrebufferFrames: Int,
    private val steadyPrebufferFrames: Int,
    maxQueueFrames: Int
) {
    private val maxTargetFrames = (maxQueueFrames - 1).coerceAtLeast(steadyPrebufferFrames)
    private var frameDurationMs: Long = 20L
    private var currentTargetFrames: Int = steadyPrebufferFrames
    private var estimatedJitterMs: Double = 0.0
    private var lastArrivalRealtimeMs: Long? = null
    private var lastSenderTimestampMs: Long? = null
    private var stablePlaybackFrames: Int = 0
    private var pressureBoostFrames: Int = 0

    fun reset(frameDurationMs: Long) {
        this.frameDurationMs = frameDurationMs.coerceAtLeast(1L)
        currentTargetFrames = steadyPrebufferFrames
        estimatedJitterMs = 0.0
        lastArrivalRealtimeMs = null
        lastSenderTimestampMs = null
        stablePlaybackFrames = 0
        pressureBoostFrames = 0
    }

    fun onFrameArrived(frame: PcmFrame, arrivalRealtimeMs: Long) {
        val previousArrival = lastArrivalRealtimeMs
        val previousTimestamp = lastSenderTimestampMs
        if (previousArrival != null && previousTimestamp != null && frame.timestampMs > previousTimestamp) {
            val arrivalDeltaMs = arrivalRealtimeMs - previousArrival
            val senderDeltaMs = (frame.timestampMs - previousTimestamp).coerceAtLeast(1L)
            val variationMs = abs(arrivalDeltaMs - senderDeltaMs).toDouble()
            estimatedJitterMs += (variationMs - estimatedJitterMs) / 8.0
            currentTargetFrames = max(currentTargetFrames, recommendedTargetFrames())
        }

        lastArrivalRealtimeMs = arrivalRealtimeMs
        lastSenderTimestampMs = frame.timestampMs
    }

    fun onGapConcealed() {
        registerPressure(extraFrames = 2)
    }

    fun onLateFrameDropped() {
        registerPressure(extraFrames = 1)
    }

    fun onQueueOverflow() {
        stablePlaybackFrames = 0
    }

    fun onAudioTrackUnderrun(underrunDelta: Int) {
        if (underrunDelta > 0) {
            registerPressure(extraFrames = (underrunDelta * 2).coerceAtMost(4))
        }
    }

    fun onPlaybackWait() {
        registerPressure(extraFrames = 1)
    }

    fun onFramePlayed(queueDepthFrames: Int) {
        val stableEnough = queueDepthFrames >= (currentTargetFrames - 1).coerceAtLeast(0)
        stablePlaybackFrames = if (stableEnough) stablePlaybackFrames + 1 else 0
        if (stablePlaybackFrames < STABLE_PLAYBACK_THRESHOLD_FRAMES) {
            return
        }

        stablePlaybackFrames = 0
        if (pressureBoostFrames > 0) {
            pressureBoostFrames--
        }

        val recommended = recommendedTargetFrames()
        if (currentTargetFrames > recommended) {
            currentTargetFrames--
        }
    }

    fun snapshot(): AdaptivePcmBufferSnapshot {
        return AdaptivePcmBufferSnapshot(
            startupTargetFrames = max(startupPrebufferFrames, currentTargetFrames),
            targetPrebufferFrames = currentTargetFrames,
            basePrebufferFrames = steadyPrebufferFrames,
            estimatedJitterMs = estimatedJitterMs.roundToInt()
        )
    }

    private fun registerPressure(extraFrames: Int) {
        pressureBoostFrames = (pressureBoostFrames + extraFrames)
            .coerceAtMost(maxTargetFrames - steadyPrebufferFrames)
        stablePlaybackFrames = 0
        currentTargetFrames = (recommendedTargetFrames() + pressureBoostFrames)
            .coerceAtMost(maxTargetFrames)
    }

    private fun recommendedTargetFrames(): Int {
        val jitterFrames = ceil(estimatedJitterMs / frameDurationMs.toDouble()).toInt()
        val safetyFrames = if (estimatedJitterMs >= frameDurationMs / 2.0) 1 else 0
        return (steadyPrebufferFrames + jitterFrames + safetyFrames)
            .coerceIn(steadyPrebufferFrames, maxTargetFrames)
    }

    private companion object {
        private const val STABLE_PLAYBACK_THRESHOLD_FRAMES = 24
    }
}
