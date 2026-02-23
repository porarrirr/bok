package com.example.p2paudio.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

@Serializable
data class SessionOfferPayload(
    val version: String = "1",
    val role: String = "sender",
    val sessionId: String,
    val senderDeviceName: String,
    val senderPubKeyFingerprint: String,
    val offerSdp: String,
    val expiresAtUnixMs: Long
)

@Serializable
data class SessionAnswerPayload(
    val version: String = "1",
    val role: String = "receiver",
    val sessionId: String,
    val receiverDeviceName: String,
    val receiverPubKeyFingerprint: String,
    val answerSdp: String,
    val expiresAtUnixMs: Long
)

@Serializable
sealed class PairingPayload {
    abstract val sessionId: String

    @Serializable
    @SerialName("offer")
    data class Offer(val data: SessionOfferPayload) : PairingPayload() {
        override val sessionId: String = data.sessionId
    }

    @Serializable
    @SerialName("answer")
    data class Answer(val data: SessionAnswerPayload) : PairingPayload() {
        override val sessionId: String = data.sessionId
    }
}
