package com.example.p2paudio.logging

import android.util.Log

object AppLogger {
    private const val BASE_TAG = "P2PAudio"

    fun d(
        category: String,
        event: String,
        message: String,
        context: Map<String, Any?> = emptyMap()
    ) = emit(Log.DEBUG, category, event, message, context, null)

    fun i(
        category: String,
        event: String,
        message: String,
        context: Map<String, Any?> = emptyMap()
    ) = emit(Log.INFO, category, event, message, context, null)

    fun w(
        category: String,
        event: String,
        message: String,
        context: Map<String, Any?> = emptyMap()
    ) = emit(Log.WARN, category, event, message, context, null)

    fun e(
        category: String,
        event: String,
        message: String,
        context: Map<String, Any?> = emptyMap(),
        throwable: Throwable? = null
    ) = emit(Log.ERROR, category, event, message, context, throwable)

    private fun emit(
        priority: Int,
        category: String,
        event: String,
        message: String,
        context: Map<String, Any?>,
        throwable: Throwable?
    ) {
        val tag = "$BASE_TAG/$category"
        val contextPart = context.entries
            .mapNotNull { (key, value) ->
                value?.toString()?.takeIf { it.isNotBlank() }?.let { "$key=$it" }
            }
            .joinToString(separator = " ")
        val body = buildString {
            append("event=").append(event)
            append(" msg=").append(message)
            if (contextPart.isNotBlank()) {
                append(" ").append(contextPart)
            }
        }

        when (priority) {
            Log.DEBUG -> Log.d(tag, body)
            Log.INFO -> Log.i(tag, body)
            Log.WARN -> Log.w(tag, body)
            Log.ERROR -> if (throwable != null) Log.e(tag, body, throwable) else Log.e(tag, body)
            else -> Log.println(priority, tag, body)
        }
    }
}
