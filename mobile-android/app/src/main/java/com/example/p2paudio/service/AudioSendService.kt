package com.example.p2paudio.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.os.Build
import android.os.IBinder
import com.example.p2paudio.R
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
        AppLogger.d(
            "AudioSendService",
            "service_start_command",
            "Received onStartCommand",
            context = mapOf("startId" to startId, "flags" to flags)
        )
        return START_STICKY
    }

    override fun onDestroy() {
        AppLogger.i("AudioSendService", "service_destroy", "Foreground service destroyed")
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
        private const val CHANNEL_ID = "p2p_audio_channel"
        private const val NOTIFICATION_ID = 3001
    }
}
