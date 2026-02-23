package com.example.p2paudio.audio

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import android.os.Process
import java.util.PriorityQueue
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.max

class AndroidPcmPlayer {
    private val running = AtomicBoolean(false)
    private val lock = Object()
    private val pendingFrames = PriorityQueue<PcmFrame>(compareBy { it.sequence })

    private var playbackThread: Thread? = null
    private var audioTrack: AudioTrack? = null
    private var expectedSequence: Int? = null
    private var lastFormatKey: String? = null
    private var frameBytes: Int = 0
    private var frameDurationMs: Long = 20

    fun enqueue(frame: PcmFrame) {
        if (frame.pcmBytes.isEmpty()) {
            return
        }

        synchronized(lock) {
            val formatKey = "${frame.sampleRate}-${frame.channels}-${frame.bitsPerSample}"
            if (lastFormatKey != null && lastFormatKey != formatKey) {
                resetUnsafe()
            }
            if (audioTrack == null) {
                audioTrack = createTrack(frame)
                frameBytes = frame.pcmBytes.size
                frameDurationMs = ((frame.frameSamplesPerChannel * 1000L) / frame.sampleRate).coerceAtLeast(1L)
                lastFormatKey = formatKey
            }

            pendingFrames.add(frame)
            if (pendingFrames.size > MAX_QUEUE_FRAMES) {
                pendingFrames.poll()
            }
            lock.notifyAll()
        }

        if (running.compareAndSet(false, true)) {
            startPlaybackLoop()
        }
    }

    fun stop() {
        running.set(false)
        synchronized(lock) {
            lock.notifyAll()
            resetUnsafe()
        }
        playbackThread?.interrupt()
        playbackThread = null
    }

    private fun startPlaybackLoop() {
        playbackThread = Thread {
            Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO)
            while (running.get()) {
                var trackToWrite: AudioTrack? = null
                var bytesToWrite: ByteArray? = null
                var shouldContinue = false

                synchronized(lock) {
                    val track = audioTrack
                    if (track == null) {
                        waitForFramesUnsafe()
                        shouldContinue = true
                    } else {
                        val nextBytes = dequeueFrameUnsafe()
                        if (nextBytes == null) {
                            waitForFramesUnsafe()
                            shouldContinue = true
                        } else {
                            trackToWrite = track
                            bytesToWrite = nextBytes
                        }
                    }
                }

                if (shouldContinue) {
                    continue
                }
                if (trackToWrite == null || bytesToWrite == null) {
                    continue
                }
                val track = trackToWrite ?: continue
                val bytes = bytesToWrite ?: continue
                track.write(bytes, 0, bytes.size, AudioTrack.WRITE_BLOCKING)
            }
        }.apply {
            name = "android-pcm-player"
            isDaemon = true
            start()
        }
    }

    private fun dequeueFrameUnsafe(): ByteArray? {
        if (pendingFrames.isEmpty()) {
            return null
        }

        val expected = expectedSequence
        if (expected == null) {
            val first = pendingFrames.poll() ?: return null
            expectedSequence = first.sequence + 1
            return first.pcmBytes
        }

        val prebufferReady = pendingFrames.size >= PREBUFFER_FRAMES
        while (pendingFrames.isNotEmpty()) {
            val head = pendingFrames.peek() ?: break
            if (head.sequence >= expected) {
                break
            }
            pendingFrames.poll()
        }

        val head = pendingFrames.peek()
        if (head != null && head.sequence == expected) {
            expectedSequence = expected + 1
            val frame = pendingFrames.poll() ?: return null
            return frame.pcmBytes
        }

        if (!prebufferReady) {
            return null
        }

        expectedSequence = expected + 1
        return ByteArray(frameBytes)
    }

    private fun createTrack(frame: PcmFrame): AudioTrack {
        val channelConfig = if (frame.channels == 1) {
            AudioFormat.CHANNEL_OUT_MONO
        } else {
            AudioFormat.CHANNEL_OUT_STEREO
        }
        val minBufferSize = AudioTrack.getMinBufferSize(
            frame.sampleRate,
            channelConfig,
            AudioFormat.ENCODING_PCM_16BIT
        )
        val bufferSize = max(minBufferSize, frame.pcmBytes.size * MIN_TRACK_BUFFER_FRAMES)
        return AudioTrack.Builder()
            .setAudioAttributes(
                AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_MEDIA)
                    .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                    .build()
            )
            .setAudioFormat(
                AudioFormat.Builder()
                    .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                    .setSampleRate(frame.sampleRate)
                    .setChannelMask(channelConfig)
                    .build()
            )
            .setTransferMode(AudioTrack.MODE_STREAM)
            .setBufferSizeInBytes(bufferSize)
            .build()
            .apply { play() }
    }

    private fun waitForFramesUnsafe() {
        lock.wait(frameDurationMs)
    }

    private fun resetUnsafe() {
        pendingFrames.clear()
        expectedSequence = null
        lastFormatKey = null
        frameBytes = 0
        audioTrack?.runCatching {
            pause()
            flush()
            stop()
            release()
        }
        audioTrack = null
    }

    companion object {
        private const val PREBUFFER_FRAMES = 2
        private const val MAX_QUEUE_FRAMES = 10
        private const val MIN_TRACK_BUFFER_FRAMES = 8
    }
}
