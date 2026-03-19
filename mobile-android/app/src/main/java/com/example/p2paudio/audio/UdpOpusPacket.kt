package com.example.p2paudio.audio

data class UdpOpusPacket(
    val sequence: Int,
    val timestampMs: Long,
    val sampleRate: Int,
    val channels: Int,
    val frameSamplesPerChannel: Int,
    val opusPayload: ByteArray
)
