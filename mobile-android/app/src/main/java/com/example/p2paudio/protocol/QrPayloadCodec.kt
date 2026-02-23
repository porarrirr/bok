package com.example.p2paudio.protocol

import com.example.p2paudio.model.SessionAnswerPayload
import com.example.p2paudio.model.SessionOfferPayload
import java.io.ByteArrayOutputStream
import java.util.Base64
import java.util.zip.Deflater
import java.util.zip.Inflater
import kotlinx.serialization.json.Json

object QrPayloadCodec {
    private val json = Json {
        ignoreUnknownKeys = false
        encodeDefaults = true
        prettyPrint = false
    }

    fun encodeOffer(payload: SessionOfferPayload): String {
        val raw = json.encodeToString(SessionOfferPayload.serializer(), payload)
        return encodeTransportString(raw)
    }

    fun encodeAnswer(payload: SessionAnswerPayload): String {
        val raw = json.encodeToString(SessionAnswerPayload.serializer(), payload)
        return encodeTransportString(raw)
    }

    fun decodeOffer(raw: String): SessionOfferPayload {
        val decoded = decodeTransportString(raw)
        return json.decodeFromString(SessionOfferPayload.serializer(), decoded)
    }

    fun decodeAnswer(raw: String): SessionAnswerPayload {
        val decoded = decodeTransportString(raw)
        return json.decodeFromString(SessionAnswerPayload.serializer(), decoded)
    }

    private fun encodeTransportString(raw: String): String {
        val utf8 = raw.toByteArray(Charsets.UTF_8)
        if (utf8.size < MIN_BYTES_FOR_COMPRESSION) {
            return raw
        }

        val compressed = compress(utf8) ?: return raw
        val encoded = Base64.getUrlEncoder().withoutPadding().encodeToString(compressed)
        if (encoded.length >= raw.length) {
            return raw
        }
        return "$COMPRESSED_PREFIX$encoded"
    }

    private fun decodeTransportString(raw: String): String {
        if (!raw.startsWith(COMPRESSED_PREFIX)) {
            return raw
        }
        val encoded = raw.removePrefix(COMPRESSED_PREFIX)
        require(encoded.isNotBlank()) { "Compressed payload is empty" }

        val compressed = try {
            Base64.getUrlDecoder().decode(encoded)
        } catch (_: IllegalArgumentException) {
            throw IllegalArgumentException("Compressed payload base64 is invalid")
        }
        val decompressed = decompress(compressed, MAX_DECOMPRESSED_BYTES)
            ?: throw IllegalArgumentException("Failed to decode compressed payload")
        return decompressed.toString(Charsets.UTF_8)
    }

    private fun compress(input: ByteArray): ByteArray? {
        val deflater = Deflater(Deflater.BEST_SPEED)
        return try {
            deflater.setInput(input)
            deflater.finish()
            val output = ByteArrayOutputStream(input.size)
            val buffer = ByteArray(1024)
            while (!deflater.finished()) {
                val count = deflater.deflate(buffer)
                if (count <= 0) {
                    break
                }
                output.write(buffer, 0, count)
            }
            if (!deflater.finished()) {
                null
            } else {
                output.toByteArray()
            }
        } catch (_: Exception) {
            null
        } finally {
            deflater.end()
        }
    }

    private fun decompress(input: ByteArray, maxBytes: Int): ByteArray? {
        val inflater = Inflater()
        return try {
            inflater.setInput(input)
            val output = ByteArrayOutputStream(input.size * 2)
            val buffer = ByteArray(1024)
            var total = 0

            while (!inflater.finished()) {
                val count = inflater.inflate(buffer)
                if (count > 0) {
                    total += count
                    if (total > maxBytes) {
                        return null
                    }
                    output.write(buffer, 0, count)
                    continue
                }
                if (inflater.needsInput() || inflater.needsDictionary()) {
                    return null
                }
            }
            output.toByteArray()
        } catch (_: Exception) {
            null
        } finally {
            inflater.end()
        }
    }

    private const val COMPRESSED_PREFIX = "p2paudio-z1:"
    private const val MIN_BYTES_FOR_COMPRESSION = 256
    private const val MAX_DECOMPRESSED_BYTES = 512_000
}
