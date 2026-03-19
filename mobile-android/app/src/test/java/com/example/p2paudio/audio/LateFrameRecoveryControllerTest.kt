package com.example.p2paudio.audio

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class LateFrameRecoveryControllerTest {

    @Test
    fun waitsForLateFrameWhileTrackHasSafeHeadroom() {
        val controller = LateFrameRecoveryController()

        assertTrue(
            controller.shouldKeepWaiting(
                expectedSequence = 42,
                frameDurationMs = 20L,
                queuedTrackFrames = 15_360,
                frameSamplesPerChannel = 960,
                nowRealtimeMs = 1_000L
            )
        )
        assertTrue(
            controller.shouldKeepWaiting(
                expectedSequence = 42,
                frameDurationMs = 20L,
                queuedTrackFrames = 11_520,
                frameSamplesPerChannel = 960,
                nowRealtimeMs = 1_120L
            )
        )
    }

    @Test
    fun stopsWaitingAfterGraceBudgetExpires() {
        val controller = LateFrameRecoveryController()

        assertTrue(
            controller.shouldKeepWaiting(
                expectedSequence = 7,
                frameDurationMs = 20L,
                queuedTrackFrames = 15_360,
                frameSamplesPerChannel = 960,
                nowRealtimeMs = 2_000L
            )
        )
        assertFalse(
            controller.shouldKeepWaiting(
                expectedSequence = 7,
                frameDurationMs = 20L,
                queuedTrackFrames = 15_360,
                frameSamplesPerChannel = 960,
                nowRealtimeMs = 2_220L
            )
        )
    }

    @Test
    fun stopsWaitingWhenTrackHeadroomFallsToSafetyMargin() {
        val controller = LateFrameRecoveryController()

        assertTrue(
            controller.shouldKeepWaiting(
                expectedSequence = 99,
                frameDurationMs = 20L,
                queuedTrackFrames = 5_760,
                frameSamplesPerChannel = 960,
                nowRealtimeMs = 3_000L
            )
        )
        assertFalse(
            controller.shouldKeepWaiting(
                expectedSequence = 99,
                frameDurationMs = 20L,
                queuedTrackFrames = 1_920,
                frameSamplesPerChannel = 960,
                nowRealtimeMs = 3_020L
            )
        )
    }

    @Test
    fun newSequenceStartsFreshWaitBudget() {
        val controller = LateFrameRecoveryController()

        assertTrue(
            controller.shouldKeepWaiting(
                expectedSequence = 10,
                frameDurationMs = 20L,
                queuedTrackFrames = 15_360,
                frameSamplesPerChannel = 960,
                nowRealtimeMs = 4_000L
            )
        )
        assertFalse(
            controller.shouldKeepWaiting(
                expectedSequence = 10,
                frameDurationMs = 20L,
                queuedTrackFrames = 15_360,
                frameSamplesPerChannel = 960,
                nowRealtimeMs = 4_220L
            )
        )

        controller.reset()

        assertTrue(
            controller.shouldKeepWaiting(
                expectedSequence = 11,
                frameDurationMs = 20L,
                queuedTrackFrames = 15_360,
                frameSamplesPerChannel = 960,
                nowRealtimeMs = 4_240L
            )
        )
    }
}
