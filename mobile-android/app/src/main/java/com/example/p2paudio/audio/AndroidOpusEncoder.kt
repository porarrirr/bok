package com.example.p2paudio.audio

import android.media.MediaCodec
import android.media.MediaCodec.BufferInfo
import android.media.MediaFormat
import com.example.p2paudio.logging.AppLogger

class AndroidOpusEncoder(
    private val sampleRate: Int,
    private val channels: Int,
    application: UdpOpusApplication
) {
    private val codec: MediaCodec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_OPUS)
    private val bufferInfo = BufferInfo()

    init {
        val bitrate = when (application) {
            UdpOpusApplication.RESTRICTED_LOWDELAY -> 64_000
            UdpOpusApplication.AUDIO -> 128_000
        }
        val complexity = when (application) {
            UdpOpusApplication.RESTRICTED_LOWDELAY -> 5
            UdpOpusApplication.AUDIO -> 10
        }
        val format = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_OPUS, sampleRate, channels).apply {
            setInteger(MediaFormat.KEY_BIT_RATE, bitrate)
            setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, sampleRate * channels * 2)
            setInteger(MediaFormat.KEY_COMPLEXITY, complexity)
        }
        codec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        codec.start()
        AppLogger.i(
            "OpusEncoder",
            "encoder_started",
            "Configured MediaCodec Opus encoder",
            context = mapOf(
                "sampleRate" to sampleRate,
                "channels" to channels,
                "bitrate" to bitrate,
                "complexity" to complexity
            )
        )
    }

    fun encodePcm(pcmBytes: ByteArray, timestampUs: Long): List<ByteArray> {
        val inputIndex = codec.dequeueInputBuffer(CODEC_TIMEOUT_US)
        require(inputIndex >= 0) { "MediaCodec did not expose an input buffer in time" }
        codec.getInputBuffer(inputIndex)?.apply {
            clear()
            put(pcmBytes)
        } ?: error("MediaCodec input buffer is null")
        codec.queueInputBuffer(inputIndex, 0, pcmBytes.size, timestampUs, 0)
        return drainOutput()
    }

    fun close() {
        runCatching {
            codec.stop()
        }
        runCatching {
            codec.release()
        }
    }

    private fun drainOutput(): List<ByteArray> {
        val payloads = mutableListOf<ByteArray>()
        while (true) {
            when (val outputIndex = codec.dequeueOutputBuffer(bufferInfo, CODEC_TIMEOUT_US)) {
                MediaCodec.INFO_TRY_AGAIN_LATER -> return payloads
                MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    AppLogger.d(
                        "OpusEncoder",
                        "output_format_changed",
                        "MediaCodec output format changed",
                        context = mapOf("format" to codec.outputFormat.toString())
                    )
                }
                else -> if (outputIndex >= 0) {
                    val outputBuffer = codec.getOutputBuffer(outputIndex)
                    if (outputBuffer != null && bufferInfo.size > 0) {
                        outputBuffer.position(bufferInfo.offset)
                        outputBuffer.limit(bufferInfo.offset + bufferInfo.size)
                        val bytes = ByteArray(bufferInfo.size)
                        outputBuffer.get(bytes)
                        payloads += bytes
                    }
                    codec.releaseOutputBuffer(outputIndex, false)
                }
            }
        }
    }

    companion object {
        private const val CODEC_TIMEOUT_US = 10_000L
    }
}
