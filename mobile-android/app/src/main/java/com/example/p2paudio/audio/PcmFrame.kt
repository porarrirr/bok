package com.example.p2paudio.audio

data class PcmFrame(
    val sequence: Int,
    val timestampMs: Long,
    val sampleRate: Int,
    val channels: Int,
    val bitsPerSample: Int,
    val frameSamplesPerChannel: Int,
    val pcmBytes: ByteArray
)
