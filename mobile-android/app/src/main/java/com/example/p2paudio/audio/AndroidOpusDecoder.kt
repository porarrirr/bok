package com.example.p2paudio.audio

import android.media.MediaCodec
import android.media.MediaCodec.BufferInfo
import android.media.MediaFormat
import com.example.p2paudio.logging.AppLogger
import java.nio.ByteBuffer
import java.nio.ByteOrder

class AndroidOpusDecoder(
    private val frameListener: (PcmFrame) -> Unit
) {
    private var decoder: MediaCodec? = null
    private var formatKey: String? = null
    private var lastWarningAtMs = 0L

    fun decode(packet: UdpOpusPacket) {
        ensureDecoder(packet)
        val codec = decoder ?: error("Opus decoder is not configured")
        val inputIndex = codec.dequeueInputBuffer(CODEC_TIMEOUT_US)
        if (inputIndex < 0) {
            logWarning(
                event = "opus_input_unavailable",
                message = "MediaCodec did not expose an input buffer in time",
                context = mapOf("sequence" to packet.sequence)
            )
            return
        }

        val inputBuffer = codec.getInputBuffer(inputIndex) ?: return
        inputBuffer.clear()
        inputBuffer.put(packet.opusPayload)
        codec.queueInputBuffer(
            inputIndex,
            0,
            packet.opusPayload.size,
            packet.timestampMs * 1_000L,
            0
        )
        drain(codec, packet)
    }

    fun close() {
        decoder?.runCatching {
            stop()
            release()
        }
        decoder = null
        formatKey = null
        lastWarningAtMs = 0L
    }

    private fun ensureDecoder(packet: UdpOpusPacket) {
        val nextKey = "${packet.sampleRate}-${packet.channels}"
        if (decoder != null && formatKey == nextKey) {
            return
        }

        close()
        val format = MediaFormat.createAudioFormat(
            MediaFormat.MIMETYPE_AUDIO_OPUS,
            packet.sampleRate,
            packet.channels
        ).apply {
            setByteBuffer("csd-0", createOpusHeader(packet.sampleRate, packet.channels))
            setByteBuffer("csd-1", ByteBuffer.wrap(ByteArray(8)))
            setByteBuffer("csd-2", ByteBuffer.wrap(ByteArray(8)))
        }

        val codec = MediaCodec.createDecoderByType(MediaFormat.MIMETYPE_AUDIO_OPUS)
        codec.configure(format, null, null, 0)
        codec.start()
        decoder = codec
        formatKey = nextKey
        AppLogger.i(
            "OpusDecoder",
            "decoder_ready",
            "Configured MediaCodec Opus decoder",
            context = mapOf(
                "sampleRate" to packet.sampleRate,
                "channels" to packet.channels
            )
        )
    }

    private fun drain(codec: MediaCodec, packet: UdpOpusPacket) {
        val bufferInfo = BufferInfo()
        while (true) {
            when (val outputIndex = codec.dequeueOutputBuffer(bufferInfo, 0)) {
                MediaCodec.INFO_TRY_AGAIN_LATER -> return
                MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    AppLogger.d(
                        "OpusDecoder",
                        "decoder_output_format_changed",
                        "Decoder output format changed",
                        context = mapOf("format" to codec.outputFormat.toString())
                    )
                }
                else -> {
                    if (outputIndex < 0) {
                        return
                    }
                    val outputBuffer = codec.getOutputBuffer(outputIndex)
                    if (outputBuffer != null && bufferInfo.size > 0) {
                        val readableBuffer = outputBuffer.duplicate().apply {
                            position(bufferInfo.offset)
                            limit(bufferInfo.offset + bufferInfo.size)
                        }
                        val pcmBytes = ByteArray(bufferInfo.size)
                        readableBuffer.get(pcmBytes)
                        frameListener(
                            PcmFrame(
                                sequence = packet.sequence,
                                timestampMs = packet.timestampMs,
                                sampleRate = packet.sampleRate,
                                channels = packet.channels,
                                bitsPerSample = 16,
                                frameSamplesPerChannel = packet.frameSamplesPerChannel,
                                pcmBytes = pcmBytes
                            )
                        )
                    }
                    codec.releaseOutputBuffer(outputIndex, false)
                }
            }
        }
    }

    private fun createOpusHeader(sampleRate: Int, channels: Int): ByteBuffer {
        return ByteBuffer.allocate(19)
            .order(ByteOrder.LITTLE_ENDIAN)
            .apply {
put("OpusHead".toByteArray())
                put(1)
                put(channels.toByte())
                putShort(312)
                putInt(sampleRate)
                putShort(0)
                put(0)
                flip()
            }
    }

    private fun logWarning(event: String, message: String, context: Map<String, Any?>) {
        val now = System.currentTimeMillis()
        if (now - lastWarningAtMs < WARNING_LOG_INTERVAL_MS) {
            return
        }
        lastWarningAtMs = now
        AppLogger.w("OpusDecoder", event, message, context)
    }

    companion object {
        private const val CODEC_TIMEOUT_US = 20_000L
        private const val WARNING_LOG_INTERVAL_MS = 1_000L
    }
}
