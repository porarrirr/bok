package com.example.p2paudio.protocol

import com.example.p2paudio.model.SessionAnswerPayload
import com.example.p2paudio.model.SessionOfferPayload
import kotlinx.serialization.json.Json

object QrPayloadCodec {
    private val json = Json {
        ignoreUnknownKeys = false
        encodeDefaults = true
        prettyPrint = false
    }

    fun encodeOffer(payload: SessionOfferPayload): String =
        json.encodeToString(SessionOfferPayload.serializer(), payload)

    fun encodeAnswer(payload: SessionAnswerPayload): String =
        json.encodeToString(SessionAnswerPayload.serializer(), payload)

    fun decodeOffer(raw: String): SessionOfferPayload =
        json.decodeFromString(SessionOfferPayload.serializer(), raw)

    fun decodeAnswer(raw: String): SessionAnswerPayload =
        json.decodeFromString(SessionAnswerPayload.serializer(), raw)
}
