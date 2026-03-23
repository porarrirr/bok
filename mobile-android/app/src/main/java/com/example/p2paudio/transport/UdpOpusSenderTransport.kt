package com.example.p2paudio.transport

import android.media.AudioRecord
import android.os.Process
import com.example.p2paudio.audio.AndroidOpusEncoder
import com.example.p2paudio.audio.UdpOpusApplication
import com.example.p2paudio.audio.UdpOpusPacket
import com.example.p2paudio.audio.UdpOpusPacketCodec
import com.example.p2paudio.logging.AppLogger
import com.example.p2paudio.model.FailureCode
import com.example.p2paudio.model.SessionFailure
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.max

class UdpOpusSenderTransport(
    private val audioRecord: AudioRecord,
    private val sampleRate: Int,
    private val channels: Int,
    private val frameDurationMs: Int,
    private val application: UdpOpusApplication,
    remoteHost: String,
    private val remotePort: Int,
    private val failureListener: (SessionFailure) -> Unit = {}
) : AudioTransport {

    override val mode: TransportMode = TransportMode.UDP_OPUS

    private val running = AtomicBoolean(false)
    private val remoteAddress = InetAddress.getByName(remoteHost)
    private var senderThread: Thread? = null
    private var socket: DatagramSocket? = null

    fun start() {
        if (!running.compareAndSet(false, true)) {
            return
        }

        val frameSamplesPerChannel = sampleRate * frameDurationMs / 1000
        val frameBytes = max(1, frameSamplesPerChannel * channels * 2)
        socket = DatagramSocket()
        val encoder = AndroidOpusEncoder(sampleRate, channels, application)
        senderThread = Thread {
            Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO)
            var sequence = 0
            val inputBuffer = ByteArray(frameBytes)

            try {
                while (running.get()) {
                    val bytesRead = audioRecord.read(inputBuffer, 0, frameBytes, AudioRecord.READ_BLOCKING)
                    if (bytesRead <= 0) {
                        continue
                    }
                    val pcm = if (bytesRead == frameBytes) inputBuffer.copyOf() else inputBuffer.copyOf(bytesRead)
                    val encodedPackets = encoder.encodePcm(
                        pcmBytes = pcm,
                        timestampUs = System.nanoTime() / 1_000L
                    )
                    for (opusPayload in encodedPackets) {
                        val packet = UdpOpusPacket(
                            sequence = sequence++,
                            timestampMs = System.currentTimeMillis(),
                            sampleRate = sampleRate,
                            channels = channels,
                            frameSamplesPerChannel = frameSamplesPerChannel,
                            opusPayload = opusPayload
                        )
                        val encoded = UdpOpusPacketCodec.encode(packet)
                        socket?.send(DatagramPacket(encoded, encoded.size, remoteAddress, remotePort))
                    }
                }
            } catch (error: Exception) {
                if (running.get()) {
                    AppLogger.e(
                        "UdpOpusSender",
                        "sender_failed",
                        "UDP Opus sender failed",
                        context = mapOf("reason" to (error.message ?: "unknown")),
                        throwable = error
                    )
                    failureListener(
                        SessionFailure(
                            FailureCode.PEER_UNREACHABLE,
                            error.message ?: "UDP Opus sender failed"
                        )
                    )
                }
            } finally {
                encoder.close()
                socket?.close()
                socket = null
            }
        }.apply {
            name = "udp-opus-sender"
            isDaemon = true
            start()
        }
    }

    override fun close() {
        running.set(false)
        senderThread?.interrupt()
        senderThread = null
        socket?.close()
        socket = null
    }
}
