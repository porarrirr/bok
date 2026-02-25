package com.example.p2paudio.model

import kotlinx.serialization.Serializable

@Serializable
data class PairingInitPayload(
    val version: String = "2",
    val phase: String = "init",
    val sessionId: String,
    val senderDeviceName: String,
    val senderPubKeyFingerprint: String,
    val offerSdp: String,
    val expiresAtUnixMs: Long
)

@Serializable
data class PairingConfirmPayload(
    val version: String = "2",
    val phase: String = "confirm",
    val sessionId: String,
    val receiverDeviceName: String,
    val receiverPubKeyFingerprint: String,
    val answerSdp: String,
    val expiresAtUnixMs: Long
)
