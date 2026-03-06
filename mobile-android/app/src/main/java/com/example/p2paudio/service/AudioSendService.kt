package com.example.p2paudio.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.os.Build
import android.os.IBinder
import com.example.p2paudio.R
import com.example.p2paudio.capture.AudioCaptureRuntime
import com.example.p2paudio.logging.AppLogger

class AudioSendService : Service() {

    override fun onCreate() {
        super.onCreate()
        AppLogger.i("AudioSendService", "service_create", "Foreground service created")
        createNotificationChannel()
        startForeground(NOTIFICATION_ID, buildNotification())
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val startIntent = intent ?: return START_NOT_STICKY
        val action = startIntent.action ?: return START_NOT_STICKY
        AppLogger.d(
            "AudioSendService",
            "service_start_command",
            "Received onStartCommand",
            context = mapOf("startId" to startId, "flags" to flags, "action" to action)
        )

        when (action) {
            ACTION_STOP_CAPTURE -> {
                AudioCaptureRuntime.stop()
                stopSelfResult(startId)
                return START_NOT_STICKY
            }

            ACTION_START_CAPTURE -> {
                val projectionData = startIntent.projectionData()
                if (projectionData == null) {
                    val error = IllegalArgumentException("Projection permission data missing")
                    AppLogger.e(
                        "AudioSendService",
                        "service_start_missing_projection_data",
                        "Projection permission data missing in start intent",
                        throwable = error
                    )
                    AudioCaptureRuntime.reportStartFailure(error)
                    stopSelfResult(startId)
                    return START_NOT_STICKY
                }

                val result = AudioCaptureRuntime.start(this, projectionData)
                if (result.isFailure) {
                    stopSelfResult(startId)
                    return START_NOT_STICKY
                }
                return START_STICKY
            }

            else -> return START_STICKY
        }
    }

    override fun onDestroy() {
        AppLogger.i("AudioSendService", "service_destroy", "Foreground service destroyed")
        AudioCaptureRuntime.stop()
        stopForeground(STOP_FOREGROUND_REMOVE)
        super.onDestroy()
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return
        }
        val manager = getSystemService(NotificationManager::class.java)
        val channel = NotificationChannel(
            CHANNEL_ID,
            getString(R.string.notification_channel_name),
            NotificationManager.IMPORTANCE_LOW
        )
        manager.createNotificationChannel(channel)
    }

    private fun buildNotification(): Notification {
        val builder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(this, CHANNEL_ID)
        } else {
            Notification.Builder(this)
        }

        return builder
            .setContentTitle(getString(R.string.notification_content_title))
            .setContentText(getString(R.string.notification_content_text))
            .setSmallIcon(android.R.drawable.ic_btn_speak_now)
            .setOngoing(true)
            .build()
    }

    companion object {
        const val ACTION_START_CAPTURE = "com.example.p2paudio.action.START_CAPTURE"
        const val ACTION_STOP_CAPTURE = "com.example.p2paudio.action.STOP_CAPTURE"
        const val EXTRA_PROJECTION_DATA = "com.example.p2paudio.extra.PROJECTION_DATA"
        private const val CHANNEL_ID = "p2p_audio_channel"
        private const val NOTIFICATION_ID = 3001
    }
}

private fun Intent.projectionData(): Intent? {
    return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
        getParcelableExtra(AudioSendService.EXTRA_PROJECTION_DATA, Intent::class.java)
    } else {
        @Suppress("DEPRECATION")
        getParcelableExtra(AudioSendService.EXTRA_PROJECTION_DATA)
    }
}
