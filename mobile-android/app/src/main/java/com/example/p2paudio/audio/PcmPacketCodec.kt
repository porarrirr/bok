package com.example.p2paudio.audio

import java.nio.ByteBuffer
import java.nio.ByteOrder

object PcmPacketCodec {
    private const val VERSION: Byte = 1
    private const val HEADER_SIZE = 22

    fun encode(frame: PcmFrame): ByteArray {
        val totalSize = HEADER_SIZE + frame.pcmBytes.size
        val buffer = ByteBuffer.allocate(totalSize).order(ByteOrder.LITTLE_ENDIAN)
        buffer.put(VERSION)
        buffer.put(frame.channels.toByte())
        buffer.putShort(frame.bitsPerSample.toShort())
        buffer.putInt(frame.sampleRate)
        buffer.putShort(frame.frameSamplesPerChannel.toShort())
        buffer.putInt(frame.sequence)
        buffer.putLong(frame.timestampMs)
        buffer.put(frame.pcmBytes)
        return buffer.array()
    }

    fun decode(packet: ByteArray): PcmFrame? {
        if (packet.size < HEADER_SIZE) {
            return null
        }
        val buffer = ByteBuffer.wrap(packet).order(ByteOrder.LITTLE_ENDIAN)
        val version = buffer.get()
        if (version != VERSION) {
            return null
        }
        val channels = buffer.get().toInt() and 0xFF
        val bitsPerSample = buffer.short.toInt() and 0xFFFF
        val sampleRate = buffer.int
        val frameSamplesPerChannel = buffer.short.toInt() and 0xFFFF
        val sequence = buffer.int
        val timestampMs = buffer.long

        if (channels !in 1..2 || bitsPerSample != 16 || sampleRate <= 0 || frameSamplesPerChannel <= 0) {
            return null
        }

        val pcmSize = packet.size - HEADER_SIZE
        if (pcmSize <= 0) {
            return null
        }
        val pcmBytes = ByteArray(pcmSize)
        buffer.get(pcmBytes)
        return PcmFrame(
            sequence = sequence,
            timestampMs = timestampMs,
            sampleRate = sampleRate,
            channels = channels,
            bitsPerSample = bitsPerSample,
            frameSamplesPerChannel = frameSamplesPerChannel,
            pcmBytes = pcmBytes
        )
    }
}
