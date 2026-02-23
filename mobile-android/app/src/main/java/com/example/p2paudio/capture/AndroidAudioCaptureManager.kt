package com.example.p2paudio.capture

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioPlaybackCaptureConfiguration
import android.media.AudioRecord
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Build

class AndroidAudioCaptureManager(
    private val context: Context
) {
    private var mediaProjection: MediaProjection? = null
    private var audioRecord: AudioRecord? = null

    fun isSupported(): Boolean = Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q

    fun start(mediaProjectionPermissionResultData: Intent): Result<AudioRecord> {
        if (!isSupported()) {
            return Result.failure(IllegalStateException("AudioPlaybackCapture requires Android 10+"))
        }

        val projectionManager = context.getSystemService(MediaProjectionManager::class.java)
        val projection = projectionManager.getMediaProjection(Activity.RESULT_OK, mediaProjectionPermissionResultData)
            ?: return Result.failure(IllegalStateException("Failed to create MediaProjection"))

        val config = AudioPlaybackCaptureConfiguration.Builder(projection)
            .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
            .addMatchingUsage(AudioAttributes.USAGE_GAME)
            .build()

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
            return Result.failure(
                IllegalStateException("Invalid AudioRecord min buffer size: $minBuffer")
            )
        }
        return runCatching {
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
            record
        }.onFailure {
            projection.stop()
        }
    }

    fun stop() {
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
    }
}
