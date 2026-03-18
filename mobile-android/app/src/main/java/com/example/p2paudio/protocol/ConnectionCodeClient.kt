package com.example.p2paudio.protocol

import com.example.p2paudio.model.ConnectionCodePayload
import com.example.p2paudio.model.FailureCode
import com.example.p2paudio.model.SessionFailure
import java.io.IOException
import java.net.HttpURLConnection
import java.net.SocketTimeoutException
import java.net.URI
import java.net.URLEncoder
import java.nio.charset.StandardCharsets
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class ConnectionCodeClientException(val failure: SessionFailure) : IOException(failure.message)

object ConnectionCodeClient {
    private const val CONNECT_TIMEOUT_MS = 10_000
    private const val READ_TIMEOUT_MS = 10_000

    suspend fun fetchInitPayload(connectionCode: ConnectionCodePayload): String = withContext(Dispatchers.IO) {
        val connection = openConnection(connectionCode, "/pairing/init").apply {
            requestMethod = "GET"
        }

        runRequest(
            connection = connection,
            successCode = HttpURLConnection.HTTP_OK,
            operationName = "fetch init payload"
        )
    }

    suspend fun submitConfirmPayload(connectionCode: ConnectionCodePayload, confirmPayload: String) = withContext(Dispatchers.IO) {
        val connection = openConnection(connectionCode, "/pairing/confirm").apply {
            requestMethod = "POST"
            doOutput = true
            setRequestProperty("Content-Type", "text/plain; charset=utf-8")
        }

        try {
            connection.outputStream.use { output ->
                output.write(confirmPayload.toByteArray(StandardCharsets.UTF_8))
            }
        } catch (error: IOException) {
            connection.disconnect()
            throw mapTransportFailure(error)
        }

        runRequest(
            connection = connection,
            successCode = HttpURLConnection.HTTP_ACCEPTED,
            operationName = "submit confirm payload"
        )
        Unit
    }

    private fun openConnection(connectionCode: ConnectionCodePayload, path: String): HttpURLConnection {
        val token = URLEncoder.encode(connectionCode.token, "UTF-8")
        val url = URI(
            "http",
            null,
            connectionCode.host,
            connectionCode.port,
            path,
            "token=$token",
            null
        ).toURL()

        return (url.openConnection() as HttpURLConnection).apply {
            connectTimeout = CONNECT_TIMEOUT_MS
            readTimeout = READ_TIMEOUT_MS
            useCaches = false
            setRequestProperty("Accept", "text/plain")
        }
    }

    private fun runRequest(
        connection: HttpURLConnection,
        successCode: Int,
        operationName: String
    ): String {
        try {
            val responseCode = connection.responseCode
            val body = readBody(connection, responseCode)
            if (responseCode == successCode) {
                return body
            }

            throw when (responseCode) {
                HttpURLConnection.HTTP_GONE -> ConnectionCodeClientException(
                    SessionFailure(
                        FailureCode.SESSION_EXPIRED,
                        "Connection code expired during $operationName"
                    )
                )
                HttpURLConnection.HTTP_BAD_REQUEST,
                HttpURLConnection.HTTP_UNAUTHORIZED,
                HttpURLConnection.HTTP_NOT_FOUND,
                HttpURLConnection.HTTP_CONFLICT -> ConnectionCodeClientException(
                    SessionFailure(
                        FailureCode.INVALID_PAYLOAD,
                        "Connection code was rejected during $operationName"
                    )
                )
                else -> ConnectionCodeClientException(
                    SessionFailure(
                        FailureCode.PEER_UNREACHABLE,
                        "Windows peer failed during $operationName: HTTP $responseCode ${body.ifBlank { "" }}".trim()
                    )
                )
            }
        } catch (failure: ConnectionCodeClientException) {
            throw failure
        } catch (error: IOException) {
            throw mapTransportFailure(error)
        } finally {
            connection.disconnect()
        }
    }

    private fun readBody(connection: HttpURLConnection, responseCode: Int): String {
        val inputStream = when {
            responseCode in 200..299 -> connection.inputStream
            connection.errorStream != null -> connection.errorStream
            else -> null
        } ?: return ""

        return inputStream.bufferedReader(StandardCharsets.UTF_8).use { reader ->
            reader.readText()
        }
    }

    private fun mapTransportFailure(error: IOException): ConnectionCodeClientException {
        if (error is SocketTimeoutException) {
            return ConnectionCodeClientException(
                SessionFailure(
                    FailureCode.PEER_UNREACHABLE,
                    "Timed out while contacting the Windows peer"
                )
            )
        }
        return ConnectionCodeClientException(
            SessionFailure(
                FailureCode.PEER_UNREACHABLE,
                error.message ?: "Failed to contact the Windows peer"
            )
        )
    }
}
