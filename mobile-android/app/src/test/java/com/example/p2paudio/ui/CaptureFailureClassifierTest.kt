package com.example.p2paudio.ui

import com.example.p2paudio.model.FailureCode
import org.junit.Assert.assertEquals
import org.junit.Test

class CaptureFailureClassifierTest {

    @Test
    fun `classifyCaptureStartFailure returns permission denied for security exception`() {
        val code = classifyCaptureStartFailure(SecurityException("RECORD_AUDIO permission is required"))
        assertEquals(FailureCode.PERMISSION_DENIED, code)
    }

    @Test
    fun `classifyCaptureStartFailure returns permission denied for wrapped security exception`() {
        val cause = SecurityException("permission denied")
        val code = classifyCaptureStartFailure(
            IllegalStateException("RECORD_AUDIO permission denied while starting capture", cause)
        )
        assertEquals(FailureCode.PERMISSION_DENIED, code)
    }

    @Test
    fun `classifyCaptureStartFailure returns unsupported for android version requirement`() {
        val code = classifyCaptureStartFailure(
            IllegalStateException("AudioPlaybackCapture requires Android 10+")
        )
        assertEquals(FailureCode.AUDIO_CAPTURE_NOT_SUPPORTED, code)
    }

    @Test
    fun `classifyCaptureStartFailure returns permission denied for media projection foreground service requirement`() {
        val code = classifyCaptureStartFailure(
            SecurityException(
                "Media projections require a foreground service of type " +
                    "ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION"
            )
        )
        assertEquals(FailureCode.PERMISSION_DENIED, code)
    }

    @Test
    fun `classifyCaptureStartFailure returns webrtc negotiation failed for unknown errors`() {
        val code = classifyCaptureStartFailure(IllegalStateException("Failed to create MediaProjection"))
        assertEquals(FailureCode.WEBRTC_NEGOTIATION_FAILED, code)
    }
}
