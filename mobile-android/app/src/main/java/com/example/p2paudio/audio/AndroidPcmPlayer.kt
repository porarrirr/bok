package com.example.p2paudio.audio

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioTrack
import android.os.Process
import com.example.p2paudio.logging.AppLogger
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
    private var playedFrames: Long = 0
    private var insertedSilenceFrames: Long = 0
    private var queueOverflowDrops: Long = 0
    private var staleFrameDrops: Long = 0
    private var lastStatsLogAtMs: Long = System.currentTimeMillis()
    private var lastWarningLogAtMs: Long = 0L

    fun enqueue(frame: PcmFrame) {
        if (frame.pcmBytes.isEmpty()) {
            return
        }

        synchronized(lock) {
            val formatKey = "${frame.sampleRate}-${frame.channels}-${frame.bitsPerSample}"
            if (lastFormatKey != null && lastFormatKey != formatKey) {
                AppLogger.i(
                    "PcmPlayer",
                    "player_format_change",
                    "PCM player format changed; resetting playback state",
                    context = mapOf("from" to lastFormatKey, "to" to formatKey)
                )
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
                queueOverflowDrops++
                logPlaybackWarning(
                    event = "player_queue_overflow",
                    message = "Dropped the oldest queued frame to cap playback latency",
                    context = mapOf(
                        "queueSize" to pendingFrames.size,
                        "queueOverflowDrops" to queueOverflowDrops
                    )
                )
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
        AppLogger.i(
            "PcmPlayer",
            "player_stop",
            "PCM player stopped",
            context = mapOf(
                "playedFrames" to playedFrames,
                "insertedSilenceFrames" to insertedSilenceFrames,
                "queueOverflowDrops" to queueOverflowDrops,
                "staleFrameDrops" to staleFrameDrops
            )
        )
        playedFrames = 0
        insertedSilenceFrames = 0
        queueOverflowDrops = 0
        staleFrameDrops = 0
        lastStatsLogAtMs = System.currentTimeMillis()
        lastWarningLogAtMs = 0L
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
                val writeResult = track.write(bytes, 0, bytes.size, AudioTrack.WRITE_BLOCKING)
                if (writeResult < 0) {
                    logPlaybackWarning(
                        event = "player_write_failed",
                        message = "AudioTrack write failed",
                        context = mapOf("writeResult" to writeResult)
                    )
                    continue
                }

                playedFrames++
                logStatsIfNeeded()
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
            if (pendingFrames.size < STARTUP_PREBUFFER_FRAMES) {
                return null
            }
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
            staleFrameDrops++
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
        insertedSilenceFrames++
        logPlaybackWarning(
            event = "player_gap_concealed",
            message = "Inserted silence for a missing PCM frame",
            context = mapOf(
                "expectedSequence" to expected,
                "pendingFrames" to pendingFrames.size,
                "insertedSilenceFrames" to insertedSilenceFrames,
                "staleFrameDrops" to staleFrameDrops
            )
        )
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
        AppLogger.i(
            "PcmPlayer",
            "player_track_create",
            "Creating AudioTrack for remote PCM playback",
            context = mapOf(
                "sampleRate" to frame.sampleRate,
                "channels" to frame.channels,
                "frameBytes" to frame.pcmBytes.size,
                "bufferSize" to bufferSize
            )
        )
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

    private fun logPlaybackWarning(event: String, message: String, context: Map<String, Any?>) {
        val now = System.currentTimeMillis()
        if (now - lastWarningLogAtMs < WARNING_LOG_INTERVAL_MS) {
            return
        }
        lastWarningLogAtMs = now
        AppLogger.w("PcmPlayer", event, message, context)
    }

    private fun logStatsIfNeeded() {
        val now = System.currentTimeMillis()
        if (now - lastStatsLogAtMs < STATS_LOG_INTERVAL_MS) {
            return
        }

        lastStatsLogAtMs = now
        AppLogger.d(
            "PcmPlayer",
            "player_stats",
            "PCM player stats",
            context = mapOf(
                "playedFrames" to playedFrames,
                "insertedSilenceFrames" to insertedSilenceFrames,
                "queueOverflowDrops" to queueOverflowDrops,
                "staleFrameDrops" to staleFrameDrops,
                "queueDepth" to synchronized(lock) { pendingFrames.size }
            )
        )
    }

    companion object {
        private const val STARTUP_PREBUFFER_FRAMES = 4
        private const val PREBUFFER_FRAMES = 4
        private const val MAX_QUEUE_FRAMES = 24
        private const val MIN_TRACK_BUFFER_FRAMES = 12
        private const val STATS_LOG_INTERVAL_MS = 5_000L
        private const val WARNING_LOG_INTERVAL_MS = 1_000L
    }
}
