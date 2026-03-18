package com.example.p2paudio.ui

import com.example.p2paudio.model.AudioStreamState
import com.example.p2paudio.model.ConnectionDiagnostics
import com.example.p2paudio.model.SessionFailure

data class MainUiState(
    val streamState: AudioStreamState = AudioStreamState.IDLE,
    val statusMessage: String = "",
    val setupMode: SetupMode = SetupMode.NONE,
    val setupStep: SetupStep = SetupStep.ENTRY,
    val initPayload: String = "",
    val confirmPayload: String = "",
    val localSenderFingerprint: String = "",
    val verificationCode: String = "",
    val pendingAnswerSdp: String = "",
    val activeSessionId: String = "",
    val failure: SessionFailure? = null,
    val connectionDiagnostics: ConnectionDiagnostics = ConnectionDiagnostics()
)

enum class SetupMode {
    NONE,
    SENDER,
    LISTENER
}

enum class SetupStep {
    ENTRY,
    PATH_DIAGNOSING,
    SENDER_SHOW_INIT,
    SENDER_VERIFY_CODE,
    LISTENER_SCAN_INIT,
    LISTENER_SHOW_CONFIRM
}
