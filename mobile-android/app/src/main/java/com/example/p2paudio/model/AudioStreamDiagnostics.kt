package com.example.p2paudio.model

enum class AudioStreamSource {
    NONE,
    WEBRTC_RECEIVE,
    UDP_OPUS_RECEIVE
}

data class AudioStreamDiagnostics(
    val source: AudioStreamSource = AudioStreamSource.NONE,
    val sampleRate: Int = 0,
    val channels: Int = 0,
    val bitsPerSample: Int = 0,
    val frameSamplesPerChannel: Int = 0,
    val frameDurationMs: Int = 0,
    val startupTargetFrames: Int = 0,
    val targetPrebufferFrames: Int = 0,
    val basePrebufferFrames: Int = 0,
    val maxQueueFrames: Int = 0,
    val queueDepthFrames: Int = 0,
    val audioTrackBufferFrames: Int = 0,
    val estimatedJitterMs: Int = 0,
    val playedFrames: Long = 0,
    val insertedSilenceFrames: Long = 0,
    val staleFrameDrops: Long = 0,
    val queueOverflowDrops: Long = 0,
    val audioTrackUnderruns: Int = 0
) {
    fun hasContent(): Boolean {
        return source != AudioStreamSource.NONE ||
            sampleRate > 0 ||
            queueDepthFrames > 0 ||
            playedFrames > 0 ||
            insertedSilenceFrames > 0 ||
            staleFrameDrops > 0 ||
            queueOverflowDrops > 0 ||
            audioTrackUnderruns > 0
    }
}
