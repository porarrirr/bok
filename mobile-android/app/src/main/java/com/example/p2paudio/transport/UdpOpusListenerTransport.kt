package com.example.p2paudio.transport

import android.content.Context
import android.os.Process
import android.os.SystemClock
import com.example.p2paudio.audio.AndroidOpusDecoder
import com.example.p2paudio.audio.PcmFrame
import com.example.p2paudio.audio.UdpOpusPacket
import com.example.p2paudio.audio.UdpOpusPacketCodec
import com.example.p2paudio.logging.AppLogger
import com.example.p2paudio.model.AudioStreamState
import com.example.p2paudio.model.ConnectionDiagnostics
import com.example.p2paudio.model.FailureCode
import com.example.p2paudio.model.SessionFailure
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.SocketException
import java.util.concurrent.atomic.AtomicBoolean

class UdpListenerTransportException(val failure: SessionFailure) : IllegalStateException(failure.message)

internal fun enqueueRealtimeDecodePacket(
    pendingPackets: ArrayDeque<UdpOpusPacket>,
    arrivalRealtimeMsBySequence: MutableMap<Int, Long>,
    packet: UdpOpusPacket,
    arrivalRealtimeMs: Long,
    maxQueuePackets: Int
): Int? {
    var droppedSequence: Int? = null
    while (pendingPackets.size >= maxQueuePackets) {
        val droppedPacket = pendingPackets.removeFirstOrNull() ?: break
        arrivalRealtimeMsBySequence.remove(droppedPacket.sequence)
        droppedSequence = droppedPacket.sequence
    }
    pendingPackets.addLast(packet)
    arrivalRealtimeMsBySequence[packet.sequence] = arrivalRealtimeMs
    return droppedSequence
}

class UdpOpusListenerTransport(
    context: Context,
    private val stateListener: (AudioStreamState, String?) -> Unit,
    private val pcmFrameListener: (PcmFrame, Long) -> Unit,
    private val diagnosticsListener: (ConnectionDiagnostics) -> Unit = {},
    private val serviceRegisteredListener: (String) -> Unit = {}
) : AudioTransport {

    override val mode: TransportMode = TransportMode.UDP_OPUS

    private val advertiser = NsdUdpReceiverAdvertiser(context)
    private val decodeLock = Object()
    private val pendingDecodePackets = ArrayDeque<UdpOpusPacket>()
    private val arrivalRealtimeMsBySequence = HashMap<Int, Long>()
    private val decoder = AndroidOpusDecoder { frame ->
        val arrivalRealtimeMs = synchronized(decodeLock) {
            arrivalRealtimeMsBySequence.remove(frame.sequence)
        } ?: SystemClock.elapsedRealtime()
        pcmFrameListener(frame, arrivalRealtimeMs)
    }
    private val running = AtomicBoolean(false)
    private var listenerThread: Thread? = null
    private var decoderThread: Thread? = null
    @Volatile
    private var socket: DatagramSocket? = null
    @Volatile
    private var streamingStarted = false

    fun startListening(advertiseService: Boolean = false) {
        if (!running.compareAndSet(false, true)) {
            return
        }

        try {
            stateListener(AudioStreamState.CONNECTING, WAITING_MESSAGE)
            diagnosticsListener(
                ConnectionDiagnostics(
                    selectedCandidatePairType = "udp_opus"
                )
            )

            val datagramSocket = DatagramSocket(null).apply {
                reuseAddress = true
                receiveBufferSize = MAX_PACKET_BYTES * SOCKET_RECEIVE_BUFFER_PACKETS
                bind(InetSocketAddress(UDP_PORT))
            }
            socket = datagramSocket
            if (advertiseService) {
                advertiser.register(
                    port = UDP_PORT,
                    onRegistered = { serviceName ->
                        serviceRegisteredListener(serviceName)
                        stateListener(
                            AudioStreamState.CONNECTING,
                            "$WAITING_MESSAGE\nサービス名: $serviceName"
                        )
                    },
                    onFailure = { error ->
                        fail(error, "受信待機の公開に失敗しました。")
                    }
                )
            }

            decoderThread = Thread {
                Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO)
                try {
                    while (running.get()) {
                        val nextPacket = waitForDecodePacket() ?: continue
                        decoder.decode(nextPacket)
                    }
                } catch (_: InterruptedException) {
                } catch (error: Exception) {
                    if (running.get()) {
                        fail(error, "UDP Opus デコードに失敗しました。")
                    }
                }
            }.apply {
                name = "udp-opus-decoder"
                start()
            }

            listenerThread = Thread {
                Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO)
                val buffer = ByteArray(MAX_PACKET_BYTES)
                while (running.get()) {
                    val activeSocket = socket ?: break
                    val packet = DatagramPacket(buffer, buffer.size)
                    try {
                        activeSocket.receive(packet)
                        val arrivalRealtimeMs = SystemClock.elapsedRealtime()
                        val decoded = UdpOpusPacketCodec.decode(packet.data.copyOf(packet.length))
                        if (decoded == null) {
                            AppLogger.w(
                                "UdpOpusListener",
                                "packet_decode_failed",
                                "Dropped invalid UDP Opus packet",
                                context = mapOf("length" to packet.length)
                            )
                            continue
                        }

                        if (!streamingStarted) {
                            streamingStarted = true
                            stateListener(AudioStreamState.STREAMING, STREAMING_MESSAGE)
                        }
                        enqueueDecodePacket(decoded, arrivalRealtimeMs)
                    } catch (_: SocketException) {
                        if (running.get()) {
                            fail(IllegalStateException("UDP socket closed unexpectedly"), "受信ソケットが閉じました。")
                        }
                    } catch (error: Exception) {
                        if (running.get()) {
                            fail(error, "UDP 受信に失敗しました。")
                        }
                    }
                }
            }.apply {
                name = "udp-opus-listener"
                isDaemon = true
                start()
            }

            AppLogger.i(
                "UdpOpusListener",
                "listener_started",
                "Started UDP Opus listener transport",
                context = mapOf(
                    "port" to UDP_PORT,
                    "advertiseService" to advertiseService
                )
            )
        } catch (error: Exception) {
            shutdown(emitEnded = false)
            throw UdpListenerTransportException(
                SessionFailure(
                    FailureCode.PEER_UNREACHABLE,
                    "UDP 受信待機の開始に失敗しました。 ${error.message.orEmpty()}".trim()
                )
            )
        }
    }

    override fun close() {
        shutdown(emitEnded = true)
    }

    private fun shutdown(emitEnded: Boolean) {
        val wasRunning = running.getAndSet(false)
        advertiser.unregister()
        socket?.close()
        socket = null
        listenerThread?.interrupt()
        decoderThread?.interrupt()
        synchronized(decodeLock) {
            pendingDecodePackets.clear()
            arrivalRealtimeMsBySequence.clear()
            decodeLock.notifyAll()
        }
        listenerThread = null
        decoderThread = null
        decoder.close()

        if (wasRunning) {
            AppLogger.i(
                "UdpOpusListener",
                "listener_stopped",
                "Stopped UDP Opus listener transport",
                context = mapOf("streamingStarted" to streamingStarted)
            )
            if (emitEnded) {
                stateListener(AudioStreamState.ENDED, null)
            }
        }
        streamingStarted = false
    }

    private fun enqueueDecodePacket(packet: UdpOpusPacket, arrivalRealtimeMs: Long) {
        var droppedSequence: Int? = null
        synchronized(decodeLock) {
            droppedSequence = enqueueRealtimeDecodePacket(
                pendingPackets = pendingDecodePackets,
                arrivalRealtimeMsBySequence = arrivalRealtimeMsBySequence,
                packet = packet,
                arrivalRealtimeMs = arrivalRealtimeMs,
                maxQueuePackets = MAX_DECODE_QUEUE_PACKETS
            )
            decodeLock.notifyAll()
        }

        if (droppedSequence != null) {
            AppLogger.w(
                "UdpOpusListener",
                "packet_queue_overflow",
                "Dropped an older UDP Opus packet to preserve realtime playback",
                context = mapOf(
                    "droppedSequence" to droppedSequence,
                    "queuedPackets" to pendingDecodePackets.size
                )
            )
        }
    }

    private fun waitForDecodePacket(): UdpOpusPacket? {
        synchronized(decodeLock) {
            while (running.get() && pendingDecodePackets.isEmpty()) {
                decodeLock.wait()
            }
            return pendingDecodePackets.removeFirstOrNull()
        }
    }

    private fun fail(error: Throwable, message: String) {
        AppLogger.e(
            "UdpOpusListener",
            "listener_failed",
            message,
            context = mapOf("reason" to (error.message ?: "unknown")),
            throwable = error
        )
        diagnosticsListener(
            ConnectionDiagnostics(
                selectedCandidatePairType = "udp_opus",
                failureHint = "peer_unreachable"
            )
        )
        stateListener(AudioStreamState.FAILED, "$message ${error.message.orEmpty()}".trim())
        shutdown(emitEnded = false)
    }

    companion object {
        const val UDP_PORT = 49_152
        internal const val MAX_OPUS_PAYLOAD_BYTES = 1_500
        internal const val MAX_PACKET_BYTES = UdpOpusPacketCodec.HEADER_BYTES + MAX_OPUS_PAYLOAD_BYTES
        internal const val MAX_DECODE_QUEUE_PACKETS = 32
        private const val SOCKET_RECEIVE_BUFFER_PACKETS = 128
        private const val WAITING_MESSAGE = "Windows からの UDP+Opus 接続を待っています。"
        private const val STREAMING_MESSAGE = "Windows のメディア音声を UDP+Opus で受信しています。"
    }
}
