package com.example.p2paudio.capture

import android.content.Context
import android.content.Intent
import android.media.AudioRecord
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow

object AudioCaptureRuntime {

    sealed interface Event {
        object Started : Event
        data class StartFailed(val error: Throwable) : Event
        object Stopped : Event
    }

    private val lock = Any()
    private var captureManager: AndroidAudioCaptureManager? = null

    private val _events = MutableSharedFlow<Event>(extraBufferCapacity = 8)
    val events: SharedFlow<Event> = _events.asSharedFlow()

    fun isSupported(): Boolean = AndroidAudioCaptureManager.isSupportedOnDevice()

    fun start(context: Context, permissionResultData: Intent): Result<Unit> {
        val manager = synchronized(lock) {
            captureManager ?: AndroidAudioCaptureManager(context.applicationContext).also {
                captureManager = it
            }
        }

        val result = manager.start(permissionResultData).map { Unit }
        if (result.isSuccess) {
            _events.tryEmit(Event.Started)
        } else {
            val error = result.exceptionOrNull() ?: IllegalStateException("Failed to start capture")
            _events.tryEmit(Event.StartFailed(error))
        }
        return result
    }

    fun reportStartFailure(error: Throwable) {
        _events.tryEmit(Event.StartFailed(error))
    }

    fun stop() {
        synchronized(lock) {
            captureManager?.stop()
        }
        _events.tryEmit(Event.Stopped)
    }

    fun currentAudioRecord(): AudioRecord? {
        return synchronized(lock) {
            captureManager?.currentAudioRecord()
        }
    }
}
