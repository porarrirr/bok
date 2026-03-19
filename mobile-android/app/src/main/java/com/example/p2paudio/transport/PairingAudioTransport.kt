package com.example.p2paudio.transport

import com.example.p2paudio.audio.PcmFrame
import com.example.p2paudio.webrtc.PeerConnectionController.LocalAnswerResult
import com.example.p2paudio.webrtc.PeerConnectionController.LocalOfferResult

interface PairingAudioTransport : AudioTransport {
    suspend fun createOfferSession(): Result<LocalOfferResult>

    suspend fun createAnswerForOffer(offerSdp: String): Result<LocalAnswerResult>

    suspend fun applyRemoteAnswer(answerSdp: String): Result<Unit>

    fun sendPcmFrame(frame: PcmFrame): Boolean
}
