package com.example.p2paudio.protocol

import java.security.MessageDigest

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
        val numeric = ((digest[0].toLong() and 0xFFL) shl 24) or
            ((digest[1].toLong() and 0xFFL) shl 16) or
            ((digest[2].toLong() and 0xFFL) shl 8) or
            (digest[3].toLong() and 0xFFL)
        val value = numeric % 1_000_000L
        return value.toString().padStart(6, '0')
    }
}
