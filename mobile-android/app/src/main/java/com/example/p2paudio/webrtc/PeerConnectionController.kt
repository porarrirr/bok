package com.example.p2paudio.webrtc

import com.example.p2paudio.audio.PcmFrame
import com.example.p2paudio.audio.PcmPacketCodec
import com.example.p2paudio.model.AudioStreamState
import java.nio.ByteBuffer
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.webrtc.DataChannel
import org.webrtc.MediaConstraints
import org.webrtc.PeerConnection
import org.webrtc.PeerConnectionFactory
import org.webrtc.RtpReceiver
import org.webrtc.RtpTransceiver
import org.webrtc.SdpObserver
import org.webrtc.SessionDescription

class PeerConnectionController(
    private val factory: PeerConnectionFactory,
    private val stateListener: (AudioStreamState, String?) -> Unit,
    private val pcmFrameListener: (PcmFrame) -> Unit
) {

    private val lock = Any()
    private var peerConnection: PeerConnection? = null
    private var audioDataChannel: DataChannel? = null
    private var currentSessionId: String? = null

    suspend fun createOfferSession(): Result<LocalOfferResult> = withContext(Dispatchers.IO) {
        stateListener(AudioStreamState.CONNECTING, null)
        runCatching {
            val peer = createPeerConnection()
            peerConnection = peer
            createLocalAudioDataChannel(peer)

            val offer = createOffer(peer)
            setLocalDescription(peer, offer)
            waitForIceComplete(peer)

            val localSdp = peer.localDescription?.description
                ?: error("Missing local offer SDP")

            val fingerprint = extractFingerprint(localSdp)
            val sessionId = UUID.randomUUID().toString()
            currentSessionId = sessionId

            LocalOfferResult(
                sessionId = sessionId,
                offerSdp = localSdp,
                localFingerprint = fingerprint
            )
        }.onFailure {
            stateListener(AudioStreamState.FAILED, it.message)
        }
    }

    suspend fun createAnswerForOffer(offerSdp: String): Result<LocalAnswerResult> = withContext(Dispatchers.IO) {
        stateListener(AudioStreamState.CONNECTING, null)
        runCatching {
            val peer = createPeerConnection()
            peerConnection = peer

            setRemoteDescription(
                peer,
                SessionDescription(SessionDescription.Type.OFFER, offerSdp)
            )
            val answer = createAnswer(peer)
            setLocalDescription(peer, answer)
            waitForIceComplete(peer)

            val localSdp = peer.localDescription?.description
                ?: error("Missing local answer SDP")

            LocalAnswerResult(
                answerSdp = localSdp,
                localFingerprint = extractFingerprint(localSdp)
            )
        }.onFailure {
            stateListener(AudioStreamState.FAILED, it.message)
        }
    }

    suspend fun applyRemoteAnswer(answerSdp: String): Result<Unit> = withContext(Dispatchers.IO) {
        runCatching {
            val peer = peerConnection ?: error("PeerConnection is not initialized")
            setRemoteDescription(peer, SessionDescription(SessionDescription.Type.ANSWER, answerSdp))
        }.onFailure {
            stateListener(AudioStreamState.FAILED, it.message)
        }
    }

    fun sendPcmFrame(frame: PcmFrame): Boolean {
        val packet = PcmPacketCodec.encode(frame)
        val channel = synchronized(lock) { audioDataChannel } ?: return false
        if (channel.state() != DataChannel.State.OPEN) {
            return false
        }
        if (channel.bufferedAmount() > MAX_BUFFERED_AMOUNT_BYTES) {
            return false
        }
        return channel.send(DataChannel.Buffer(ByteBuffer.wrap(packet), true))
    }

    fun close() {
        synchronized(lock) {
            audioDataChannel?.unregisterObserver()
            audioDataChannel?.close()
            audioDataChannel = null
        }
        peerConnection?.close()
        peerConnection = null
        currentSessionId = null
        stateListener(AudioStreamState.ENDED, null)
    }

    private fun createLocalAudioDataChannel(peer: PeerConnection) {
        val init = DataChannel.Init().apply {
            ordered = true
            maxRetransmits = 0
        }
        val channel = peer.createDataChannel(AUDIO_CHANNEL_LABEL, init)
            ?: error("Failed to create audio data channel")
        bindAudioDataChannel(channel)
    }

    private fun bindAudioDataChannel(channel: DataChannel) {
        synchronized(lock) {
            audioDataChannel?.unregisterObserver()
            audioDataChannel?.close()
            audioDataChannel = channel
        }

        channel.registerObserver(object : DataChannel.Observer {
            override fun onBufferedAmountChange(previousAmount: Long) = Unit

            override fun onStateChange() {
                if (channel.state() == DataChannel.State.OPEN) {
                    stateListener(AudioStreamState.STREAMING, null)
                }
            }

            override fun onMessage(buffer: DataChannel.Buffer?) {
                if (buffer == null || !buffer.binary) {
                    return
                }
                val bytes = ByteArray(buffer.data.remaining())
                buffer.data.get(bytes)
                val frame = PcmPacketCodec.decode(bytes) ?: return
                pcmFrameListener(frame)
            }
        })
    }

    private fun createPeerConnection(): PeerConnection {
        val rtcConfig = PeerConnection.RTCConfiguration(emptyList()).apply {
            sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN
            continualGatheringPolicy = PeerConnection.ContinualGatheringPolicy.GATHER_ONCE
            tcpCandidatePolicy = PeerConnection.TcpCandidatePolicy.DISABLED
            bundlePolicy = PeerConnection.BundlePolicy.MAXBUNDLE
        }

        return requireNotNull(
            factory.createPeerConnection(
                rtcConfig,
                object : PeerConnection.Observer {
                    override fun onSignalingChange(state: PeerConnection.SignalingState?) = Unit

                    override fun onIceConnectionChange(state: PeerConnection.IceConnectionState?) {
                        when (state) {
                            PeerConnection.IceConnectionState.CONNECTED,
                            PeerConnection.IceConnectionState.COMPLETED -> {
                                stateListener(AudioStreamState.STREAMING, null)
                            }

                            PeerConnection.IceConnectionState.DISCONNECTED -> {
                                stateListener(AudioStreamState.INTERRUPTED, "Peer disconnected")
                            }

                            PeerConnection.IceConnectionState.FAILED -> {
                                stateListener(AudioStreamState.FAILED, "ICE connection failed")
                            }

                            else -> Unit
                        }
                    }

                    override fun onIceConnectionReceivingChange(receiving: Boolean) = Unit
                    override fun onIceGatheringChange(state: PeerConnection.IceGatheringState?) = Unit
                    override fun onIceCandidate(candidate: org.webrtc.IceCandidate?) = Unit
                    override fun onIceCandidatesRemoved(candidates: Array<out org.webrtc.IceCandidate>?) = Unit
                    override fun onAddStream(mediaStream: org.webrtc.MediaStream?) = Unit
                    override fun onRemoveStream(mediaStream: org.webrtc.MediaStream?) = Unit
                    override fun onRenegotiationNeeded() = Unit
                    override fun onAddTrack(receiver: RtpReceiver?, mediaStreams: Array<out org.webrtc.MediaStream>?) = Unit
                    override fun onTrack(transceiver: RtpTransceiver?) = Unit

                    override fun onDataChannel(dataChannel: DataChannel?) {
                        if (dataChannel == null) {
                            return
                        }
                        if (dataChannel.label() == AUDIO_CHANNEL_LABEL) {
                            bindAudioDataChannel(dataChannel)
                        }
                    }
                }
            )
        ) { "Failed to create PeerConnection" }
    }

    private fun createOffer(peer: PeerConnection): SessionDescription {
        val latch = CountDownLatch(1)
        var result: SessionDescription? = null
        var failure: String? = null

        peer.createOffer(object : SdpObserver {
            override fun onCreateSuccess(sessionDescription: SessionDescription?) {
                result = sessionDescription
                latch.countDown()
            }

            override fun onCreateFailure(error: String?) {
                failure = error
                latch.countDown()
            }

            override fun onSetSuccess() = Unit
            override fun onSetFailure(error: String?) = Unit
        }, MediaConstraints())

        if (!latch.await(8, TimeUnit.SECONDS)) {
            error("Timeout creating offer")
        }
        failure?.let { error("Offer creation failed: $it") }
        return result ?: error("Offer creation returned null")
    }

    private fun createAnswer(peer: PeerConnection): SessionDescription {
        val latch = CountDownLatch(1)
        var result: SessionDescription? = null
        var failure: String? = null

        peer.createAnswer(object : SdpObserver {
            override fun onCreateSuccess(sessionDescription: SessionDescription?) {
                result = sessionDescription
                latch.countDown()
            }

            override fun onCreateFailure(error: String?) {
                failure = error
                latch.countDown()
            }

            override fun onSetSuccess() = Unit
            override fun onSetFailure(error: String?) = Unit
        }, MediaConstraints())

        if (!latch.await(8, TimeUnit.SECONDS)) {
            error("Timeout creating answer")
        }
        failure?.let { error("Answer creation failed: $it") }
        return result ?: error("Answer creation returned null")
    }

    private fun setLocalDescription(peer: PeerConnection, sdp: SessionDescription) {
        val latch = CountDownLatch(1)
        var failure: String? = null

        peer.setLocalDescription(object : SdpObserver {
            override fun onSetSuccess() {
                latch.countDown()
            }

            override fun onSetFailure(error: String?) {
                failure = error
                latch.countDown()
            }

            override fun onCreateSuccess(sessionDescription: SessionDescription?) = Unit
            override fun onCreateFailure(error: String?) = Unit
        }, sdp)

        if (!latch.await(8, TimeUnit.SECONDS)) {
            error("Timeout setting local SDP")
        }
        failure?.let { error("Setting local SDP failed: $it") }
    }

    private fun setRemoteDescription(peer: PeerConnection, sdp: SessionDescription) {
        val latch = CountDownLatch(1)
        var failure: String? = null

        peer.setRemoteDescription(object : SdpObserver {
            override fun onSetSuccess() {
                latch.countDown()
            }

            override fun onSetFailure(error: String?) {
                failure = error
                latch.countDown()
            }

            override fun onCreateSuccess(sessionDescription: SessionDescription?) = Unit
            override fun onCreateFailure(error: String?) = Unit
        }, sdp)

        if (!latch.await(8, TimeUnit.SECONDS)) {
            error("Timeout setting remote SDP")
        }
        failure?.let { error("Setting remote SDP failed: $it") }
    }

    private fun waitForIceComplete(peer: PeerConnection) {
        val deadlineMs = System.currentTimeMillis() + ICE_GATHER_TIMEOUT_MS
        while (System.currentTimeMillis() < deadlineMs) {
            if (peer.iceGatheringState() == PeerConnection.IceGatheringState.COMPLETE) {
                return
            }
            Thread.sleep(30)
        }
        error("ICE gathering did not complete in time")
    }

    private fun extractFingerprint(sdp: String): String {
        return sdp.lineSequence()
            .firstOrNull { it.startsWith("a=fingerprint:") }
            ?.removePrefix("a=fingerprint:")
            ?.trim()
            ?: "unknown"
    }

    data class LocalOfferResult(
        val sessionId: String,
        val offerSdp: String,
        val localFingerprint: String
    )

    data class LocalAnswerResult(
        val answerSdp: String,
        val localFingerprint: String
    )

    companion object {
        private const val ICE_GATHER_TIMEOUT_MS = 8_000L
        private const val AUDIO_CHANNEL_LABEL = "audio-pcm"
        private const val MAX_BUFFERED_AMOUNT_BYTES = 256_000L
    }
}
