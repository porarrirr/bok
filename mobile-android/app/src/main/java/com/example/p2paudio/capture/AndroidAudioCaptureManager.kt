package com.example.p2paudio.capture

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioPlaybackCaptureConfiguration
import android.media.AudioRecord
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Build
import com.example.p2paudio.logging.AppLogger

class AndroidAudioCaptureManager(
    private val context: Context
) {
    private var mediaProjection: MediaProjection? = null
    private var audioRecord: AudioRecord? = null

    fun isSupported(): Boolean = Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q

    fun start(mediaProjectionPermissionResultData: Intent): Result<AudioRecord> {
        AppLogger.i("CaptureManager", "capture_start_attempt", "Audio capture start requested")
        if (!isSupported()) {
            AppLogger.w("CaptureManager", "capture_not_supported", "Audio capture unsupported on this Android version")
            return Result.failure(IllegalStateException("AudioPlaybackCapture requires Android 10+"))
        }
        if (context.checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            AppLogger.w("CaptureManager", "record_permission_missing", "RECORD_AUDIO permission is missing")
            return Result.failure(SecurityException("RECORD_AUDIO permission is required"))
        }

        val projectionManager = context.getSystemService(MediaProjectionManager::class.java)
        val projection = projectionManager.getMediaProjection(Activity.RESULT_OK, mediaProjectionPermissionResultData)
            ?: return Result.failure(IllegalStateException("Failed to create MediaProjection"))

        val configBuilder = AudioPlaybackCaptureConfiguration.Builder(projection)
        CAPTURE_USAGES.forEach { usage ->
            runCatching {
                configBuilder.addMatchingUsage(usage)
            }
        }
        val config = configBuilder.build()

        val format = AudioFormat.Builder()
            .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
            .setSampleRate(SAMPLE_RATE)
            .setChannelMask(AudioFormat.CHANNEL_IN_STEREO)
            .build()

        val minBuffer = AudioRecord.getMinBufferSize(
            SAMPLE_RATE,
            AudioFormat.CHANNEL_IN_STEREO,
            AudioFormat.ENCODING_PCM_16BIT
        )
        if (minBuffer <= 0) {
            projection.stop()
            AppLogger.e(
                "CaptureManager",
                "invalid_min_buffer",
                "AudioRecord min buffer is invalid",
                context = mapOf("minBuffer" to minBuffer)
            )
            return Result.failure(
                IllegalStateException("Invalid AudioRecord min buffer size: $minBuffer")
            )
        }

        return try {
            val record = AudioRecord.Builder()
                .setAudioPlaybackCaptureConfig(config)
                .setAudioFormat(format)
                .setBufferSizeInBytes(minBuffer * 2)
                .build()

            record.startRecording()
            if (record.recordingState != AudioRecord.RECORDSTATE_RECORDING) {
                record.release()
                throw IllegalStateException("AudioRecord failed to enter recording state")
            }

            mediaProjection = projection
            audioRecord = record
            AppLogger.i(
                "CaptureManager",
                "capture_started",
                "Audio capture started",
                context = mapOf("sampleRate" to SAMPLE_RATE, "channels" to 2, "bufferBytes" to (minBuffer * 2))
            )
            Result.success(record)
        } catch (e: SecurityException) {
            projection.stop()
            AppLogger.e(
                "CaptureManager",
                "capture_start_security_error",
                "Security exception while starting capture",
                throwable = e
            )
            Result.failure(IllegalStateException("RECORD_AUDIO permission denied while starting capture", e))
        } catch (e: Exception) {
            projection.stop()
            AppLogger.e(
                "CaptureManager",
                "capture_start_error",
                "Unexpected exception while starting capture",
                throwable = e
            )
            Result.failure(e)
        }
    }

    fun stop() {
        AppLogger.i("CaptureManager", "capture_stop", "Stopping audio capture")
        audioRecord?.runCatching {
            stop()
            release()
        }
        audioRecord = null

        mediaProjection?.stop()
        mediaProjection = null
    }

    fun currentAudioRecord(): AudioRecord? = audioRecord

    companion object {
        const val SAMPLE_RATE = 48_000
        private val CAPTURE_USAGES = intArrayOf(
            AudioAttributes.USAGE_MEDIA,
            AudioAttributes.USAGE_GAME,
            AudioAttributes.USAGE_UNKNOWN,
            AudioAttributes.USAGE_NOTIFICATION,
            AudioAttributes.USAGE_NOTIFICATION_RINGTONE,
            AudioAttributes.USAGE_ASSISTANCE_SONIFICATION
        )
    }
}
