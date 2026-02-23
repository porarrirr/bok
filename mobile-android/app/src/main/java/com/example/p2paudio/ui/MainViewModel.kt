package com.example.p2paudio.ui

import android.app.Application
import android.content.Intent
import android.media.projection.MediaProjectionManager
import android.os.Build
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.example.p2paudio.audio.AndroidPcmPlayer
import com.example.p2paudio.audio.AndroidPcmSender
import com.example.p2paudio.capture.AndroidAudioCaptureManager
import com.example.p2paudio.model.AudioStreamState
import com.example.p2paudio.model.FailureCode
import com.example.p2paudio.model.SessionAnswerPayload
import com.example.p2paudio.model.SessionFailure
import com.example.p2paudio.model.SessionOfferPayload
import com.example.p2paudio.protocol.PairingPayloadValidator
import com.example.p2paudio.protocol.QrPayloadCodec
import com.example.p2paudio.webrtc.PeerConnectionController
import com.example.p2paudio.webrtc.WebRtcFactoryProvider
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

class MainViewModel(application: Application) : AndroidViewModel(application) {

    private val captureManager = AndroidAudioCaptureManager(application)
    private val pcmPlayer = AndroidPcmPlayer()
    private var pcmSender: AndroidPcmSender? = null
    private var playbackMessageShown = false

    private val peerController = PeerConnectionController(
        factory = WebRtcFactoryProvider.create(application),
        stateListener = { state, message ->
            _uiState.update {
                it.copy(
                    streamState = state,
                    statusMessage = message ?: state.name
                )
            }
            when (state) {
                AudioStreamState.STREAMING -> startPcmSenderIfReady()
                AudioStreamState.FAILED,
                AudioStreamState.ENDED -> stopPcmSender()
                else -> Unit
            }
        },
        pcmFrameListener = { frame ->
            pcmPlayer.enqueue(frame)
            if (!playbackMessageShown) {
                playbackMessageShown = true
                _uiState.update { it.copy(statusMessage = "Receiving remote audio") }
            }
        }
    )

    private val _uiState = MutableStateFlow(MainUiState())
    val uiState: StateFlow<MainUiState> = _uiState.asStateFlow()

    private val _commands = MutableSharedFlow<UiCommand>()
    val commands: SharedFlow<UiCommand> = _commands.asSharedFlow()

    fun requestProjectionPermission() {
        if (!captureManager.isSupported()) {
            reportFailure(
                SessionFailure(
                    FailureCode.AUDIO_CAPTURE_NOT_SUPPORTED,
                    "AudioPlaybackCapture is available on Android 10+ only"
                )
            )
            return
        }

        val manager = getApplication<Application>().getSystemService(MediaProjectionManager::class.java)
        val captureIntent = manager.createScreenCaptureIntent()
        viewModelScope.launch {
            _commands.emit(UiCommand.RequestProjectionPermission(captureIntent))
        }
    }

    fun onProjectionPermissionResult(resultData: Intent?): Boolean {
        if (resultData == null) {
            reportFailure(SessionFailure(FailureCode.PERMISSION_DENIED, "Screen capture permission denied"))
            return false
        }

        val captureResult = captureManager.start(resultData)
        if (captureResult.isFailure) {
            reportFailure(
                SessionFailure(
                    FailureCode.AUDIO_CAPTURE_NOT_SUPPORTED,
                    captureResult.exceptionOrNull()?.message ?: "Failed to start capture"
                )
            )
            return false
        }

        _uiState.update { it.copy(streamState = AudioStreamState.CAPTURING, statusMessage = "Capturing") }
        createOfferPayload()
        return true
    }

    private fun createOfferPayload() {
        viewModelScope.launch {
            val result = peerController.createOfferSession()
            result.onSuccess { local ->
                val payload = SessionOfferPayload(
                    sessionId = local.sessionId,
                    senderDeviceName = Build.MODEL ?: "android",
                    senderPubKeyFingerprint = local.localFingerprint,
                    offerSdp = local.offerSdp,
                    expiresAtUnixMs = System.currentTimeMillis() + PAYLOAD_TTL_MS
                )

                _uiState.update {
                    it.copy(
                        activeSessionId = payload.sessionId,
                        offerPayload = QrPayloadCodec.encodeOffer(payload),
                        statusMessage = "Offer generated. Let receiver scan QR.",
                        failure = null
                    )
                }
            }.onFailure {
                reportFailure(
                    SessionFailure(
                        FailureCode.WEBRTC_NEGOTIATION_FAILED,
                        it.message ?: "Failed to create offer"
                    )
                )
            }
        }
    }

    fun createAnswerFromOffer(offerRaw: String) {
        viewModelScope.launch {
            runCatching {
                val offer = QrPayloadCodec.decodeOffer(offerRaw)
                PairingPayloadValidator.validateOffer(offer, System.currentTimeMillis())?.let { failure ->
                    reportFailure(failure)
                    return@launch
                }

                val answerResult = peerController.createAnswerForOffer(offer.offerSdp)
                answerResult.onSuccess { local ->
                    val answerPayload = SessionAnswerPayload(
                        sessionId = offer.sessionId,
                        receiverDeviceName = Build.MODEL ?: "android",
                        receiverPubKeyFingerprint = local.localFingerprint,
                        answerSdp = local.answerSdp,
                        expiresAtUnixMs = System.currentTimeMillis() + PAYLOAD_TTL_MS
                    )
                    _uiState.update {
                        it.copy(
                            activeSessionId = offer.sessionId,
                            answerPayload = QrPayloadCodec.encodeAnswer(answerPayload),
                            statusMessage = "Answer generated. Sender should scan QR.",
                            failure = null
                        )
                    }
                }.onFailure {
                    reportFailure(
                        SessionFailure(
                            FailureCode.WEBRTC_NEGOTIATION_FAILED,
                            it.message ?: "Failed to create answer"
                        )
                    )
                }
            }.onFailure {
                reportFailure(SessionFailure(FailureCode.INVALID_PAYLOAD, "Invalid offer payload"))
            }
        }
    }

    fun applyAnswer(answerRaw: String) {
        viewModelScope.launch {
            runCatching {
                val answer = QrPayloadCodec.decodeAnswer(answerRaw)
                val currentSession = uiState.value.activeSessionId
                PairingPayloadValidator.validateAnswer(
                    payload = answer,
                    expectedSessionId = currentSession,
                    nowUnixMs = System.currentTimeMillis()
                )?.let { failure ->
                    reportFailure(failure)
                    return@launch
                }

                peerController.applyRemoteAnswer(answer.answerSdp)
                    .onFailure {
                        reportFailure(
                            SessionFailure(
                                FailureCode.WEBRTC_NEGOTIATION_FAILED,
                                it.message ?: "Failed to apply answer"
                            )
                        )
                    }
            }.onFailure {
                reportFailure(SessionFailure(FailureCode.INVALID_PAYLOAD, "Invalid answer payload"))
            }
        }
    }

    fun stopSession() {
        stopPcmSender()
        pcmPlayer.stop()
        playbackMessageShown = false
        captureManager.stop()
        peerController.close()
        _uiState.value = MainUiState(statusMessage = "Session ended")
    }

    override fun onCleared() {
        stopSession()
        super.onCleared()
    }

    private fun startPcmSenderIfReady() {
        if (pcmSender != null) {
            return
        }
        val audioRecord = captureManager.currentAudioRecord() ?: return
        val sender = AndroidPcmSender(
            audioRecord = audioRecord,
            sampleRate = AndroidAudioCaptureManager.SAMPLE_RATE,
            channels = 2,
            bitsPerSample = 16,
            frameDurationMs = 20,
            sendFrame = { frame -> peerController.sendPcmFrame(frame) }
        )
        sender.start()
        pcmSender = sender
        _uiState.update { it.copy(statusMessage = "Streaming captured device audio") }
    }

    private fun stopPcmSender() {
        pcmSender?.stop()
        pcmSender = null
    }

    private fun reportFailure(failure: SessionFailure) {
        stopPcmSender()
        _uiState.update {
            it.copy(
                streamState = AudioStreamState.FAILED,
                statusMessage = failure.message,
                failure = failure
            )
        }
    }

    sealed interface UiCommand {
        data class RequestProjectionPermission(val captureIntent: Intent) : UiCommand
    }

    companion object {
        private const val PAYLOAD_TTL_MS = 60_000L
    }
}
