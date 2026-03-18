package com.example.p2paudio.audio

import android.media.AudioRecord
import android.os.Process
import com.example.p2paudio.logging.AppLogger
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.max

class AndroidPcmSender(
    private val audioRecord: AudioRecord,
    private val sampleRate: Int,
    private val channels: Int,
    private val bitsPerSample: Int = 16,
    private val frameDurationMs: Int = 20,
    private val sendFrame: (PcmFrame) -> Boolean
) {
    private val running = AtomicBoolean(false)
    private var senderThread: Thread? = null
    @Volatile
    private var totalSentFrames: Long = 0
    @Volatile
    private var totalDroppedFrames: Long = 0

    fun start() {
        if (!running.compareAndSet(false, true)) {
            return
        }
        totalSentFrames = 0
        totalDroppedFrames = 0
        val frameSamplesPerChannel = sampleRate * frameDurationMs / 1000
        val bytesPerSample = bitsPerSample / 8
        val frameBytes = max(1, frameSamplesPerChannel * channels * bytesPerSample)
        AppLogger.i(
            "PcmSender",
            "sender_start",
            "Starting PCM sender",
            context = mapOf(
                "sampleRate" to sampleRate,
                "channels" to channels,
                "bitsPerSample" to bitsPerSample,
                "frameDurationMs" to frameDurationMs,
                "frameBytes" to frameBytes
            )
        )

        senderThread = Thread {
            Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO)
            var sequence = 0
            val frameBuffer = ByteArray(frameBytes)
            var lastStatsLogAtMs = System.currentTimeMillis()
            var lastReadIssueLogAtMs = 0L
            var lastSendDropLogAtMs = 0L

            while (running.get()) {
                val bytesRead = audioRecord.read(frameBuffer, 0, frameBytes, AudioRecord.READ_BLOCKING)
                if (bytesRead <= 0) {
                    val now = System.currentTimeMillis()
                    if (bytesRead < 0 && now - lastReadIssueLogAtMs >= READ_ISSUE_LOG_INTERVAL_MS) {
                        lastReadIssueLogAtMs = now
                        AppLogger.w(
                            "PcmSender",
                            "audio_record_read_issue",
                            "AudioRecord returned a non-success read result",
                            context = mapOf("bytesRead" to bytesRead)
                        )
                    }
                    continue
                }

                val pcm = if (bytesRead == frameBytes) {
                    frameBuffer.copyOf()
                } else {
                    ByteArray(frameBytes).apply {
                        System.arraycopy(frameBuffer, 0, this, 0, bytesRead)
                    }
                }

                val currentSequence = sequence++
                val sent = sendFrame(
                    PcmFrame(
                        sequence = currentSequence,
                        timestampMs = System.currentTimeMillis(),
                        sampleRate = sampleRate,
                        channels = channels,
                        bitsPerSample = bitsPerSample,
                        frameSamplesPerChannel = frameSamplesPerChannel,
                        pcmBytes = pcm
                    )
                )
                if (sent) {
                    totalSentFrames++
                } else {
                    totalDroppedFrames++
                    val now = System.currentTimeMillis()
                    if (now - lastSendDropLogAtMs >= SEND_DROP_LOG_INTERVAL_MS) {
                        lastSendDropLogAtMs = now
                        AppLogger.w(
                            "PcmSender",
                            "pcm_send_failed",
                            "PCM frame send failed",
                            context = mapOf(
                                "sequence" to currentSequence,
                                "bytesRead" to bytesRead,
                                "totalDroppedFrames" to totalDroppedFrames
                            )
                        )
                    }
                }

                val now = System.currentTimeMillis()
                if (now - lastStatsLogAtMs >= STATS_LOG_INTERVAL_MS) {
                    lastStatsLogAtMs = now
                    AppLogger.d(
                        "PcmSender",
                        "sender_stats",
                        "PCM sender stats",
                        context = mapOf(
                            "sentFrames" to totalSentFrames,
                            "droppedFrames" to totalDroppedFrames,
                            "lastSequence" to (currentSequence + 1)
                        )
                    )
                }
            }
        }.apply {
            name = "android-pcm-sender"
            isDaemon = true
            start()
        }
    }

    fun stop() {
        running.set(false)
        senderThread?.interrupt()
        senderThread = null
        AppLogger.i(
            "PcmSender",
            "sender_stop",
            "PCM sender stopped",
            context = mapOf(
                "sentFrames" to totalSentFrames,
                "droppedFrames" to totalDroppedFrames
            )
        )
    }

    companion object {
        private const val STATS_LOG_INTERVAL_MS = 5_000L
        private const val SEND_DROP_LOG_INTERVAL_MS = 1_000L
        private const val READ_ISSUE_LOG_INTERVAL_MS = 1_000L
    }
}
