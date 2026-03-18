package com.example.p2paudio.model

data class ConnectionCodePayload(
    val host: String,
    val port: Int,
    val token: String,
    val expiresAtUnixMs: Long
)
