package com.example.p2paudio.protocol

import com.example.p2paudio.model.ConnectionCodePayload

object ConnectionCodeCodec {
    const val PREFIX = "p2paudio-c1:"

    fun looksLikeConnectionCode(raw: String?): Boolean {
        return !raw.isNullOrBlank() && raw.startsWith(PREFIX)
    }

    fun encode(payload: ConnectionCodePayload): String {
        require(payload.host.isNotBlank()) { "Connection code host is blank" }
        require(payload.port in 1..65535) { "Connection code port is out of range" }
        require(payload.token.isNotBlank()) { "Connection code token is blank" }
        require(payload.expiresAtUnixMs > 0L) { "Connection code expiry is invalid" }

        return "$PREFIX${payload.host}:${payload.port}:${payload.expiresAtUnixMs}:${payload.token}"
    }

    fun decode(raw: String): ConnectionCodePayload {
        require(raw.startsWith(PREFIX)) { "Connection code prefix is invalid" }

        val body = raw.removePrefix(PREFIX)
        val parts = body.split(":", limit = 4)
        require(parts.size == 4) { "Connection code format is invalid" }

        val host = parts[0].trim()
        val port = parts[1].trim().toIntOrNull()
        val expiresAtUnixMs = parts[2].trim().toLongOrNull()
        val token = parts[3].trim()

        require(host.isNotBlank()) { "Connection code host is blank" }
        require(port != null && port in 1..65535) { "Connection code port is invalid" }
        require(expiresAtUnixMs != null && expiresAtUnixMs > 0L) { "Connection code expiry is invalid" }
        require(token.isNotBlank()) { "Connection code token is blank" }

        return ConnectionCodePayload(
            host = host,
            port = port,
            token = token,
            expiresAtUnixMs = expiresAtUnixMs
        )
    }
}
