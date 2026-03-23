package com.example.p2paudio.protocol

import com.example.p2paudio.model.ConnectionCodePayload
import com.example.p2paudio.webrtc.NetworkPathClassifier
import java.io.BufferedReader
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.ServerSocket
import java.net.Socket
import java.net.URI
import java.security.SecureRandom
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

class ConnectionCodeSessionFactory {

    fun create(initPayload: String, expiresAtUnixMs: Long): ConnectionCodeSession {
        require(initPayload.isNotBlank())
        require(expiresAtUnixMs > 0L)

        val token = createRandomToken()
        val candidates = resolveCandidateAddresses()
        for (address in candidates) {
            runCatching {
                val serverSocket = ServerSocket(0, 50, address).apply {
                    reuseAddress = true
                }
                return AndroidConnectionCodeSession(
                    serverSocket = serverSocket,
                    initPayload = initPayload,
                    payload = ConnectionCodePayload(
                        host = address.hostAddress ?: "",
                        port = serverSocket.localPort,
                        token = token,
                        expiresAtUnixMs = expiresAtUnixMs
                    )
                )
            }
        }

        throw IllegalStateException(
            "接続コードを公開できるローカル IP が見つかりませんでした。Wi-Fi または USB テザリングを確認してください。"
        )
    }

    private fun resolveCandidateAddresses(): List<InetAddress> {
        val preferredPath = NetworkPathClassifier.classifyFromLocalInterfaces()
        return runCatching { NetworkInterface.getNetworkInterfaces()?.toList().orEmpty() }
            .getOrDefault(emptyList())
            .filter { it.isUp && !it.isLoopback }
            .flatMap { iface ->
                iface.inetAddresses.toList()
                    .filterIsInstance<Inet4Address>()
                    .filterNot { it.isLoopbackAddress }
                    .map { address ->
                        val score = when {
                            preferredPath.name.contains("USB") && iface.name.contains("rndis", true) -> 2
                            preferredPath.name.contains("WIFI") && iface.name.contains("wlan", true) -> 2
                            else -> 1
                        }
                        score to address
                    }
            }
            .distinctBy { it.second.hostAddress }
            .sortedByDescending { it.first }
            .map { it.second }
    }

    private fun createRandomToken(): String {
        val bytes = ByteArray(12)
        SecureRandom().nextBytes(bytes)
        return java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)
    }

    private class AndroidConnectionCodeSession(
        private val serverSocket: ServerSocket,
        private val initPayload: String,
        payload: ConnectionCodePayload
    ) : ConnectionCodeSession {

        private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        private val confirmDeferred = CompletableDeferred<ConnectionCodeSubmission>()
        private var closed = false

        override val connectionCode: String = ConnectionCodeCodec.encode(payload)
        override val expiresAtUnixMs: Long = payload.expiresAtUnixMs
        private val token: String = payload.token

        init {
            scope.launch {
                acceptLoop()
            }
        }

        override suspend fun waitForConfirmPayload(): ConnectionCodeSubmission {
            return confirmDeferred.await()
        }

        override fun close() {
            if (closed) return
            closed = true
            serverSocket.close()
            confirmDeferred.cancel()
            scope.cancel()
        }

        private fun acceptLoop() {
            while (!serverSocket.isClosed) {
                val client = runCatching { serverSocket.accept() }.getOrNull() ?: break
                scope.launch { handleClient(client) }
            }
        }

        private fun handleClient(client: Socket) {
            client.use { socket ->
                val reader = BufferedReader(InputStreamReader(socket.getInputStream(), Charsets.UTF_8))
                val writer = OutputStreamWriter(socket.getOutputStream(), Charsets.UTF_8)
                runCatching {
                    val requestLine = reader.readLine() ?: error("Missing HTTP request line")
                    val requestParts = requestLine.split(' ', limit = 3)
                    require(requestParts.size >= 2) { "Invalid HTTP request line" }

                    val headers = linkedMapOf<String, String>()
                    while (true) {
                        val headerLine = reader.readLine() ?: error("Unexpected end of headers")
                        if (headerLine.isEmpty()) break
                        val separatorIndex = headerLine.indexOf(':')
                        if (separatorIndex > 0) {
                            headers[headerLine.substring(0, separatorIndex).trim()] =
                                headerLine.substring(separatorIndex + 1).trim()
                        }
                    }

                    val uri = URI("http://local${requestParts[1]}")
                    val queryToken = uri.rawQuery
                        ?.split('&')
                        ?.mapNotNull {
                            val pieces = it.split('=', limit = 2)
                            if (pieces.firstOrNull() == "token") pieces.getOrNull(1) else null
                        }
                        ?.firstOrNull()
                        ?.let { java.net.URLDecoder.decode(it, "UTF-8") }
                        .orEmpty()

                    if (System.currentTimeMillis() > expiresAtUnixMs) {
                        writeResponse(writer, 410, "Gone", "expired")
                        return
                    }
                    if (queryToken != token) {
                        writeResponse(writer, 401, "Unauthorized", "invalid_token")
                        return
                    }

                    if (requestParts[0] == "GET" && uri.path == "/pairing/init") {
                        writeResponse(writer, 200, "OK", initPayload)
                        return
                    }

                    if (requestParts[0] == "POST" && uri.path == "/pairing/confirm") {
                        val contentLength = headers["Content-Length"]?.toIntOrNull() ?: 0
                        val body = if (contentLength > 0) {
                            CharArray(contentLength).also { reader.read(it, 0, contentLength) }.concatToString()
                        } else {
                            ""
                        }
                        if (body.isBlank()) {
                            writeResponse(writer, 400, "Bad Request", "missing_body")
                            return
                        }

                        val accepted = confirmDeferred.complete(
                            ConnectionCodeSubmission(
                                payload = body,
                                remoteAddress = socket.inetAddress.hostAddress.orEmpty()
                            )
                        )
                        if (!accepted) {
                            writeResponse(writer, 409, "Conflict", "already_confirmed")
                            return
                        }

                        writeResponse(writer, 202, "Accepted", "ok")
                        return
                    }

                    writeResponse(writer, 404, "Not Found", "not_found")
                }.getOrElse {
                    runCatching {
                        writeResponse(writer, 500, "Internal Server Error", "internal_error")
                    }
                }
            }
        }

        private fun writeResponse(writer: OutputStreamWriter, statusCode: Int, statusText: String, body: String) {
            val bodyBytes = body.toByteArray(Charsets.UTF_8)
            writer.write(
                "HTTP/1.1 $statusCode $statusText\r\n" +
                    "Content-Type: text/plain; charset=utf-8\r\n" +
                    "Content-Length: ${bodyBytes.size}\r\n" +
                    "Connection: close\r\n\r\n" +
                    body
            )
            writer.flush()
        }
    }
}
