package com.example.p2paudio.ui

import com.example.p2paudio.model.AudioStreamState
import com.example.p2paudio.model.SessionFailure

data class MainUiState(
    val streamState: AudioStreamState = AudioStreamState.IDLE,
    val statusMessage: String = "Ready",
    val offerPayload: String = "",
    val answerPayload: String = "",
    val activeSessionId: String = "",
    val failure: SessionFailure? = null
)
