package com.example.p2paudio.audio

import android.media.AudioRecord
import android.os.Process
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

    fun start() {
        if (!running.compareAndSet(false, true)) {
            return
        }
        val frameSamplesPerChannel = sampleRate * frameDurationMs / 1000
        val bytesPerSample = bitsPerSample / 8
        val frameBytes = max(1, frameSamplesPerChannel * channels * bytesPerSample)

        senderThread = Thread {
            Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO)
            var sequence = 0
            val frameBuffer = ByteArray(frameBytes)

            while (running.get()) {
                val bytesRead = audioRecord.read(frameBuffer, 0, frameBytes, AudioRecord.READ_BLOCKING)
                if (bytesRead <= 0) {
                    continue
                }

                val pcm = if (bytesRead == frameBytes) {
                    frameBuffer.copyOf()
                } else {
                    ByteArray(frameBytes).apply {
                        System.arraycopy(frameBuffer, 0, this, 0, bytesRead)
                    }
                }

                sendFrame(
                    PcmFrame(
                        sequence = sequence++,
                        timestampMs = System.currentTimeMillis(),
                        sampleRate = sampleRate,
                        channels = channels,
                        bitsPerSample = bitsPerSample,
                        frameSamplesPerChannel = frameSamplesPerChannel,
                        pcmBytes = pcm
                    )
                )
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
    }
}
