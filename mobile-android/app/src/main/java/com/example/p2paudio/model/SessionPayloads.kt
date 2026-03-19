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

@Serializable
data class UdpInitPayload(
    val version: String = "2",
    val phase: String = "udp_init",
    val transport: String = "udp_opus",
    val sessionId: String,
    val senderDeviceName: String,
    val expiresAtUnixMs: Long
)

@Serializable
data class UdpConfirmPayload(
    val version: String = "2",
    val phase: String = "udp_confirm",
    val transport: String = "udp_opus",
    val sessionId: String,
    val receiverDeviceName: String,
    val receiverPort: Int,
    val expiresAtUnixMs: Long
)
