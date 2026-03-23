package com.example.p2paudio.protocol

import java.io.Closeable

data class ConnectionCodeSubmission(
    val payload: String,
    val remoteAddress: String
)

interface ConnectionCodeSession : Closeable {
    val connectionCode: String
    val expiresAtUnixMs: Long
    suspend fun waitForConfirmPayload(): ConnectionCodeSubmission
}
