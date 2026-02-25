package com.example.p2paudio.ui

import android.app.Application
import android.content.Intent
import android.media.projection.MediaProjectionManager
import android.os.Build
import androidx.annotation.StringRes
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.example.p2paudio.R
import com.example.p2paudio.audio.AndroidPcmPlayer
import com.example.p2paudio.audio.AndroidPcmSender
import com.example.p2paudio.capture.AndroidAudioCaptureManager
import com.example.p2paudio.logging.AppLogger
import com.example.p2paudio.model.AudioStreamState
import com.example.p2paudio.model.FailureCode
import com.example.p2paudio.model.PairingConfirmPayload
import com.example.p2paudio.model.PairingInitPayload
import com.example.p2paudio.model.SessionFailure
import com.example.p2paudio.protocol.PairingPayloadValidator
import com.example.p2paudio.protocol.QrPayloadCodec
import com.example.p2paudio.protocol.VerificationCode
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

    private val _uiState = MutableStateFlow(
        MainUiState(statusMessage = text(R.string.status_ready))
    )
    val uiState: StateFlow<MainUiState> = _uiState.asStateFlow()

    private val _commands = MutableSharedFlow<UiCommand>()
    val commands: SharedFlow<UiCommand> = _commands.asSharedFlow()

    private val peerController = PeerConnectionController(
        factory = WebRtcFactoryProvider.create(application),
        stateListener = { state, message ->
            AppLogger.i(
                "MainViewModel",
                "stream_state_update",
                "Stream state updated",
                context = mapOf(
                    "state" to state.name,
                    "message" to (message ?: "")
                )
            )
            _uiState.update {
                val nextMessage = when {
                    state == AudioStreamState.STREAMING && it.statusMessage.isNotBlank() -> it.statusMessage
                    message != null -> localizePeerMessage(message)
                    else -> stateToStatusLabel(state)
                }
                it.copy(streamState = state, statusMessage = nextMessage)
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
                AppLogger.i(
                    "MainViewModel",
                    "receiver_first_frame",
                    "First remote audio frame received",
                    context = mapOf(
                        "sampleRate" to frame.sampleRate,
                        "channels" to frame.channels,
                        "bitsPerSample" to frame.bitsPerSample
                    )
                )
                _uiState.update { it.copy(statusMessage = text(R.string.status_receiving_remote_audio)) }
            }
        }
    )

    fun beginListenerFlow() {
        AppLogger.i("MainViewModel", "listener_flow_start", "Listener flow selected")
        _uiState.update {
            it.copy(
                setupMode = SetupMode.LISTENER,
                setupStep = SetupStep.LISTENER_SCAN_INIT,
                initPayload = "",
                confirmPayload = "",
                localSenderFingerprint = "",
                verificationCode = "",
                pendingAnswerSdp = "",
                activeSessionId = "",
                failure = null,
                statusMessage = text(R.string.status_listener_ready_to_scan)
            )
        }
    }

    fun requestProjectionPermission() {
        AppLogger.i("MainViewModel", "request_projection_permission", "Requesting projection permission")
        if (!captureManager.isSupported()) {
            recoverToEntry(
                SessionFailure(
                    FailureCode.AUDIO_CAPTURE_NOT_SUPPORTED,
                    text(R.string.error_audio_capture_android10)
                )
            )
            return
        }

        _uiState.update {
            it.copy(
                setupMode = SetupMode.SENDER,
                setupStep = SetupStep.SENDER_SHOW_INIT,
                initPayload = "",
                confirmPayload = "",
                verificationCode = "",
                pendingAnswerSdp = "",
                activeSessionId = "",
                failure = null,
                statusMessage = text(R.string.status_capturing)
            )
        }

        val manager = getApplication<Application>().getSystemService(MediaProjectionManager::class.java)
        val captureIntent = manager.createScreenCaptureIntent()
        viewModelScope.launch {
            _commands.emit(UiCommand.RequestProjectionPermission(captureIntent))
        }
    }

    fun onProjectionPermissionResult(resultData: Intent?): Boolean {
        if (resultData == null) {
            AppLogger.w("MainViewModel", "projection_permission_denied", "Projection permission denied by user")
            recoverToEntry(
                SessionFailure(
                    FailureCode.PERMISSION_DENIED,
                    text(R.string.error_permission_denied)
                )
            )
            return false
        }

        AppLogger.i("MainViewModel", "capture_start_attempt", "Starting audio capture")
        val captureResult = captureManager.start(resultData)
        if (captureResult.isFailure) {
            val cause = captureResult.exceptionOrNull()
            AppLogger.e(
                "MainViewModel",
                "capture_start_failed",
                "Failed to start audio capture",
                context = mapOf("reason" to (cause?.message ?: "unknown")),
                throwable = cause
            )
            recoverToEntry(
                SessionFailure(
                    FailureCode.AUDIO_CAPTURE_NOT_SUPPORTED,
                    captureResult.exceptionOrNull()?.message ?: text(R.string.error_capture_start_failed)
                )
            )
            return false
        }

        AppLogger.i("MainViewModel", "capture_started", "Audio capture started")
        _uiState.update {
            it.copy(
                streamState = AudioStreamState.CAPTURING,
                statusMessage = text(R.string.status_capturing)
            )
        }
        createInitPayload()
        return true
    }

    private fun createInitPayload() {
        AppLogger.i("MainViewModel", "init_generation_start", "Creating local init payload")
        viewModelScope.launch {
            val result = peerController.createOfferSession()
            result.onSuccess { local ->
                val payload = PairingInitPayload(
                    sessionId = local.sessionId,
                    senderDeviceName = Build.MODEL ?: "android",
                    senderPubKeyFingerprint = local.localFingerprint,
                    offerSdp = local.offerSdp,
                    expiresAtUnixMs = System.currentTimeMillis() + PAYLOAD_TTL_MS
                )

                _uiState.update {
                    it.copy(
                        setupMode = SetupMode.SENDER,
                        setupStep = SetupStep.SENDER_SHOW_INIT,
                        activeSessionId = payload.sessionId,
                        localSenderFingerprint = local.localFingerprint,
                        initPayload = QrPayloadCodec.encodeInit(payload),
                        confirmPayload = "",
                        verificationCode = "",
                        pendingAnswerSdp = "",
                        statusMessage = text(R.string.status_init_generated),
                        failure = null
                    )
                }
                AppLogger.i(
                    "MainViewModel",
                    "init_generation_success",
                    "Init payload generated",
                    context = mapOf(
                        "sessionId" to payload.sessionId,
                        "offerLength" to payload.offerSdp.length,
                        "fingerprintHead" to local.localFingerprint.take(16)
                    )
                )
            }.onFailure {
                AppLogger.e(
                    "MainViewModel",
                    "init_generation_failed",
                    "Init payload generation failed",
                    context = mapOf("reason" to (it.message ?: "unknown")),
                    throwable = it
                )
                recoverToEntry(
                    SessionFailure(
                        FailureCode.WEBRTC_NEGOTIATION_FAILED,
                        it.message ?: text(R.string.error_create_offer_failed)
                    )
                )
            }
        }
    }

    fun createConfirmFromInit(initRaw: String) {
        AppLogger.i(
            "MainViewModel",
            "confirm_generation_start",
            "Creating confirm payload from init QR",
            context = mapOf("initLength" to initRaw.length)
        )
        beginListenerFlow()
        viewModelScope.launch {
            runCatching {
                val init = QrPayloadCodec.decodeInit(initRaw)
                PairingPayloadValidator.validateInit(init, System.currentTimeMillis())?.let { failure ->
                    AppLogger.w(
                        "MainViewModel",
                        "init_validation_failed",
                        "Init payload validation failed",
                        context = mapOf(
                            "sessionId" to init.sessionId,
                            "failureCode" to failure.code.name,
                            "reason" to failure.message
                        )
                    )
                    recoverToEntry(failure, payloadRole = PayloadRole.INIT)
                    return@launch
                }

                val answerResult = peerController.createAnswerForOffer(init.offerSdp)
                answerResult.onSuccess { local ->
                    val confirmPayload = PairingConfirmPayload(
                        sessionId = init.sessionId,
                        receiverDeviceName = Build.MODEL ?: "android",
                        receiverPubKeyFingerprint = local.localFingerprint,
                        answerSdp = local.answerSdp,
                        expiresAtUnixMs = System.currentTimeMillis() + PAYLOAD_TTL_MS
                    )
                    val verificationCode = VerificationCode.fromSessionAndFingerprints(
                        sessionId = init.sessionId,
                        senderFingerprint = init.senderPubKeyFingerprint,
                        receiverFingerprint = local.localFingerprint
                    )
                    _uiState.update {
                        it.copy(
                            setupMode = SetupMode.LISTENER,
                            setupStep = SetupStep.LISTENER_SHOW_CONFIRM,
                            activeSessionId = init.sessionId,
                            confirmPayload = QrPayloadCodec.encodeConfirm(confirmPayload),
                            verificationCode = verificationCode,
                            statusMessage = text(R.string.status_confirm_generated),
                            failure = null
                        )
                    }
                    AppLogger.i(
                        "MainViewModel",
                        "confirm_generation_success",
                        "Confirm payload generated",
                        context = mapOf(
                            "sessionId" to init.sessionId,
                            "answerLength" to confirmPayload.answerSdp.length,
                            "fingerprintHead" to local.localFingerprint.take(16)
                        )
                    )
                }.onFailure {
                    AppLogger.e(
                        "MainViewModel",
                        "confirm_generation_failed",
                        "Confirm payload generation failed",
                        context = mapOf("reason" to (it.message ?: "unknown")),
                        throwable = it
                    )
                    recoverToEntry(
                        SessionFailure(
                            FailureCode.WEBRTC_NEGOTIATION_FAILED,
                            it.message ?: text(R.string.error_create_answer_failed)
                        )
                    )
                }
            }.onFailure {
                AppLogger.w(
                    "MainViewModel",
                    "init_decode_failed",
                    "Init payload decode failed",
                    context = mapOf("reason" to (it.message ?: "unknown"))
                )
                recoverToEntry(
                    SessionFailure(FailureCode.INVALID_PAYLOAD, text(R.string.error_invalid_init_payload)),
                    payloadRole = PayloadRole.INIT
                )
            }
        }
    }

    fun applyConfirm(confirmRaw: String) {
        AppLogger.i(
            "MainViewModel",
            "confirm_apply_prepare",
            "Preparing remote confirm payload",
            context = mapOf(
                "confirmLength" to confirmRaw.length,
                "sessionId" to uiState.value.activeSessionId
            )
        )
        viewModelScope.launch {
            runCatching {
                val confirm = QrPayloadCodec.decodeConfirm(confirmRaw)
                val currentSession = uiState.value.activeSessionId
                PairingPayloadValidator.validateConfirm(
                    payload = confirm,
                    expectedSessionId = currentSession,
                    nowUnixMs = System.currentTimeMillis()
                )?.let { failure ->
                    AppLogger.w(
                        "MainViewModel",
                        "confirm_validation_failed",
                        "Confirm payload validation failed",
                        context = mapOf(
                            "sessionId" to currentSession,
                            "failureCode" to failure.code.name,
                            "reason" to failure.message
                        )
                    )
                    recoverToEntry(failure, payloadRole = PayloadRole.CONFIRM)
                    return@launch
                }

                val senderFingerprint = uiState.value.localSenderFingerprint
                if (senderFingerprint.isBlank()) {
                    recoverToEntry(
                        SessionFailure(FailureCode.INVALID_PAYLOAD, text(R.string.error_invalid_confirm_payload)),
                        payloadRole = PayloadRole.CONFIRM
                    )
                    return@launch
                }

                val verificationCode = VerificationCode.fromSessionAndFingerprints(
                    sessionId = currentSession,
                    senderFingerprint = senderFingerprint,
                    receiverFingerprint = confirm.receiverPubKeyFingerprint
                )
                _uiState.update {
                    it.copy(
                        setupMode = SetupMode.SENDER,
                        setupStep = SetupStep.SENDER_VERIFY_CODE,
                        pendingAnswerSdp = confirm.answerSdp,
                        verificationCode = verificationCode,
                        statusMessage = text(R.string.status_verification_ready),
                        failure = null
                    )
                }
            }.onFailure {
                AppLogger.w(
                    "MainViewModel",
                    "confirm_decode_failed",
                    "Confirm payload decode failed",
                    context = mapOf("reason" to (it.message ?: "unknown"))
                )
                recoverToEntry(
                    SessionFailure(FailureCode.INVALID_PAYLOAD, text(R.string.error_invalid_confirm_payload)),
                    payloadRole = PayloadRole.CONFIRM
                )
            }
        }
    }

    fun approveVerificationAndConnect() {
        val answerSdp = uiState.value.pendingAnswerSdp
        if (answerSdp.isBlank()) {
            recoverToEntry(
                SessionFailure(FailureCode.INVALID_PAYLOAD, text(R.string.error_invalid_confirm_payload)),
                payloadRole = PayloadRole.CONFIRM
            )
            return
        }
        AppLogger.i(
            "MainViewModel",
            "confirm_apply_start",
            "Applying confirmed remote answer",
            context = mapOf("sessionId" to uiState.value.activeSessionId)
        )
        viewModelScope.launch {
            peerController.applyRemoteAnswer(answerSdp)
                .onSuccess {
                    AppLogger.i(
                        "MainViewModel",
                        "confirm_apply_success",
                        "Remote answer applied after verification",
                        context = mapOf("sessionId" to uiState.value.activeSessionId)
                    )
                    _uiState.update {
                        it.copy(
                            setupMode = SetupMode.SENDER,
                            setupStep = SetupStep.SENDER_SHOW_INIT,
                            pendingAnswerSdp = "",
                            statusMessage = text(R.string.status_answer_applied)
                        )
                    }
                }
                .onFailure {
                    AppLogger.e(
                        "MainViewModel",
                        "confirm_apply_failed",
                        "Remote answer apply failed",
                        context = mapOf("reason" to (it.message ?: "unknown")),
                        throwable = it
                    )
                    recoverToEntry(
                        SessionFailure(
                            FailureCode.WEBRTC_NEGOTIATION_FAILED,
                            it.message ?: text(R.string.error_apply_answer_failed)
                        )
                    )
                }
        }
    }

    fun rejectVerificationAndRestart() {
        val mismatch = text(R.string.status_verification_mismatch)
        resetToEntry(
            statusMessage = mismatch,
            failure = SessionFailure(FailureCode.INVALID_PAYLOAD, mismatch)
        )
    }

    fun stopSession() {
        AppLogger.i(
            "MainViewModel",
            "session_stop",
            "Stopping current session",
            context = mapOf("sessionId" to uiState.value.activeSessionId)
        )
        stopPcmSender()
        pcmPlayer.stop()
        playbackMessageShown = false
        captureManager.stop()
        peerController.close()
        _uiState.value = MainUiState(statusMessage = text(R.string.status_session_ended))
    }

    override fun onCleared() {
        stopSession()
        super.onCleared()
    }

    private fun startPcmSenderIfReady() {
        if (pcmSender != null) {
            AppLogger.d("MainViewModel", "sender_already_running", "PCM sender already running")
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
        AppLogger.i("MainViewModel", "sender_started", "PCM sender started")
        _uiState.update { it.copy(statusMessage = text(R.string.status_streaming_captured_audio)) }
    }

    private fun stopPcmSender() {
        if (pcmSender != null) {
            AppLogger.i("MainViewModel", "sender_stopped", "PCM sender stopped")
        }
        pcmSender?.stop()
        pcmSender = null
    }

    private fun recoverToEntry(failure: SessionFailure, payloadRole: PayloadRole? = null) {
        val localizedMessage = localizeFailureMessage(failure, payloadRole)
        AppLogger.e(
            "MainViewModel",
            "session_failure",
            "Recoverable setup failure; reset to entry",
            context = mapOf(
                "code" to failure.code.name,
                "message" to localizedMessage,
                "sessionId" to uiState.value.activeSessionId
            )
        )
        resetToEntry(
            statusMessage = localizedMessage,
            failure = failure.copy(message = localizedMessage)
        )
    }

    private fun resetToEntry(statusMessage: String, failure: SessionFailure?) {
        stopPcmSender()
        pcmPlayer.stop()
        playbackMessageShown = false
        captureManager.stop()
        peerController.close()
        _uiState.value = MainUiState(
            statusMessage = statusMessage,
            failure = failure
        )
    }

    private fun localizePeerMessage(message: String): String = when (message) {
        "Peer disconnected" -> text(R.string.status_peer_disconnected)
        "ICE connection failed" -> text(R.string.status_ice_connection_failed)
        else -> text(R.string.error_webrtc_negotiation_failed)
    }

    private fun stateToStatusLabel(state: AudioStreamState): String = when (state) {
        AudioStreamState.IDLE -> text(R.string.state_idle)
        AudioStreamState.CAPTURING -> text(R.string.state_capturing)
        AudioStreamState.CONNECTING -> text(R.string.state_connecting)
        AudioStreamState.STREAMING -> text(R.string.state_streaming)
        AudioStreamState.INTERRUPTED -> text(R.string.state_interrupted)
        AudioStreamState.FAILED -> text(R.string.state_failed)
        AudioStreamState.ENDED -> text(R.string.state_ended)
    }

    private fun localizeFailureMessage(failure: SessionFailure, payloadRole: PayloadRole?): String {
        return when (failure.code) {
            FailureCode.PERMISSION_DENIED -> text(R.string.error_permission_denied)
            FailureCode.AUDIO_CAPTURE_NOT_SUPPORTED -> text(R.string.error_audio_capture_not_supported)
            FailureCode.WEBRTC_NEGOTIATION_FAILED -> text(R.string.error_webrtc_negotiation_failed)
            FailureCode.PEER_UNREACHABLE -> text(R.string.error_peer_unreachable)
            FailureCode.NETWORK_CHANGED -> text(R.string.error_network_changed)
            FailureCode.SESSION_EXPIRED -> text(R.string.error_session_expired)
            FailureCode.INVALID_PAYLOAD -> when (payloadRole) {
                PayloadRole.INIT -> text(R.string.error_invalid_init_payload)
                PayloadRole.CONFIRM -> text(R.string.error_invalid_confirm_payload)
                null -> text(R.string.error_invalid_payload)
            }
        }
    }

    private fun text(@StringRes resId: Int, vararg args: Any): String {
        return getApplication<Application>().getString(resId, *args)
    }

    sealed interface UiCommand {
        data class RequestProjectionPermission(val captureIntent: Intent) : UiCommand
    }

    private enum class PayloadRole {
        INIT,
        CONFIRM
    }

    companion object {
        private const val PAYLOAD_TTL_MS = 60_000L
    }
}
