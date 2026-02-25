package com.example.p2paudio.protocol

import java.security.MessageDigest
import kotlin.math.abs

object VerificationCode {

    fun fromSessionAndFingerprints(
        sessionId: String,
        senderFingerprint: String,
        receiverFingerprint: String
    ): String {
        val normalized = listOf(sessionId, senderFingerprint, receiverFingerprint)
            .joinToString("|")
            .toByteArray(Charsets.UTF_8)
        val digest = MessageDigest.getInstance("SHA-256").digest(normalized)
        val numeric = ((digest[0].toInt() and 0xFF) shl 24) or
            ((digest[1].toInt() and 0xFF) shl 16) or
            ((digest[2].toInt() and 0xFF) shl 8) or
            (digest[3].toInt() and 0xFF)
        val value = abs(numeric.toLong()) % 1_000_000L
        return value.toString().padStart(6, '0')
    }
}
