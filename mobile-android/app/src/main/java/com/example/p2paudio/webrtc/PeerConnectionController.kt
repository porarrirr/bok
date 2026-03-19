package com.example.p2paudio.webrtc

import com.example.p2paudio.audio.PcmFrame
import com.example.p2paudio.audio.PcmPacketCodec
import com.example.p2paudio.logging.AppLogger
import com.example.p2paudio.model.AudioStreamState
import com.example.p2paudio.model.ConnectionDiagnostics
import com.example.p2paudio.model.NetworkPathType
import com.example.p2paudio.transport.PairingAudioTransport
import com.example.p2paudio.transport.TransportMode
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
import org.webrtc.IceCandidate

class PeerConnectionController(
    private val factory: PeerConnectionFactory,
    private val stateListener: (AudioStreamState, String?) -> Unit,
    private val pcmFrameListener: (PcmFrame) -> Unit,
    private val diagnosticsListener: (ConnectionDiagnostics) -> Unit = {}
) : PairingAudioTransport {

    override val mode: TransportMode = TransportMode.WEBRTC

    private val lock = Any()
    private var peerConnection: PeerConnection? = null
    private var audioDataChannel: DataChannel? = null
    private var currentSessionId: String? = null
    private var lastSendDropLogAtMs = 0L
    private var lastBufferPressureLogAtMs = 0L
    private var connectionDiagnostics = ConnectionDiagnostics()

    override suspend fun createOfferSession(): Result<LocalOfferResult> = withContext(Dispatchers.IO) {
        AppLogger.i("PeerConnection", "create_offer_start", "Creating local offer session")
        resetDiagnostics()
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

            AppLogger.i(
                "PeerConnection",
                "create_offer_success",
                "Created local offer session",
                context = mapOf(
                    "sessionId" to sessionId,
                    "offerLength" to localSdp.length,
                    "fingerprintHead" to fingerprint.take(16)
                )
            )
            LocalOfferResult(
                sessionId = sessionId,
                offerSdp = localSdp,
                localFingerprint = fingerprint
            )
        }.onFailure {
            AppLogger.e(
                "PeerConnection",
                "create_offer_failure",
                "Failed to create local offer session",
                context = mapOf("reason" to (it.message ?: "unknown")),
                throwable = it
            )
            stateListener(AudioStreamState.FAILED, it.message)
        }
    }

    override suspend fun createAnswerForOffer(offerSdp: String): Result<LocalAnswerResult> = withContext(Dispatchers.IO) {
        AppLogger.i(
            "PeerConnection",
            "create_answer_start",
            "Creating local answer for remote offer",
            context = mapOf("offerLength" to offerSdp.length)
        )
        resetDiagnostics()
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

            AppLogger.i(
                "PeerConnection",
                "create_answer_success",
                "Created local answer",
                context = mapOf(
                    "answerLength" to localSdp.length,
                    "fingerprintHead" to extractFingerprint(localSdp).take(16)
                )
            )
            LocalAnswerResult(
                answerSdp = localSdp,
                localFingerprint = extractFingerprint(localSdp)
            )
        }.onFailure {
            AppLogger.e(
                "PeerConnection",
                "create_answer_failure",
                "Failed to create local answer",
                context = mapOf("reason" to (it.message ?: "unknown")),
                throwable = it
            )
            stateListener(AudioStreamState.FAILED, it.message)
        }
    }

    override suspend fun applyRemoteAnswer(answerSdp: String): Result<Unit> = withContext(Dispatchers.IO) {
        AppLogger.i(
            "PeerConnection",
            "apply_answer_start",
            "Applying remote answer",
            context = mapOf("answerLength" to answerSdp.length, "sessionId" to currentSessionId)
        )
        runCatching {
            val peer = peerConnection ?: error("PeerConnection is not initialized")
            setRemoteDescription(peer, SessionDescription(SessionDescription.Type.ANSWER, answerSdp))
            AppLogger.i(
                "PeerConnection",
                "apply_answer_success",
                "Remote answer applied",
                context = mapOf("sessionId" to currentSessionId)
            )
        }.onFailure {
            AppLogger.e(
                "PeerConnection",
                "apply_answer_failure",
                "Failed to apply remote answer",
                context = mapOf("reason" to (it.message ?: "unknown")),
                throwable = it
            )
            stateListener(AudioStreamState.FAILED, it.message)
        }
    }

    override fun sendPcmFrame(frame: PcmFrame): Boolean {
        val packet = PcmPacketCodec.encode(frame)
        val channel = synchronized(lock) { audioDataChannel } ?: return false
        if (channel.state() != DataChannel.State.OPEN) {
            logDroppedFrame(
                reason = "data_channel_not_open",
                context = mapOf("state" to channel.state().name)
            )
            return false
        }
        if (channel.bufferedAmount() > MAX_BUFFERED_AMOUNT_BYTES) {
            logDroppedFrame(
                reason = "buffered_amount_exceeded",
                context = mapOf("bufferedAmount" to channel.bufferedAmount())
            )
            return false
        }
        val sent = channel.send(DataChannel.Buffer(ByteBuffer.wrap(packet), true))
        if (!sent) {
            logDroppedFrame(
                reason = "data_channel_send_false",
                context = mapOf("bufferedAmount" to channel.bufferedAmount())
            )
        }
        return sent
    }

    override fun close() {
        AppLogger.i(
            "PeerConnection",
            "peer_close",
            "Closing peer connection",
            context = mapOf("sessionId" to currentSessionId)
        )
        synchronized(lock) {
            audioDataChannel?.unregisterObserver()
            audioDataChannel?.close()
            audioDataChannel = null
        }
        peerConnection?.close()
        peerConnection = null
        currentSessionId = null
        resetDiagnostics()
        stateListener(AudioStreamState.ENDED, null)
    }

    private fun createLocalAudioDataChannel(peer: PeerConnection) {
        val init = DataChannel.Init().apply {
            ordered = true
            maxRetransmits = 0
        }
        val channel = peer.createDataChannel(AUDIO_CHANNEL_LABEL, init)
            ?: error("Failed to create audio data channel")
        AppLogger.i(
            "PeerConnection",
            "data_channel_create",
            "Created local audio data channel",
            context = mapOf(
                "ordered" to init.ordered,
                "maxRetransmits" to init.maxRetransmits
            )
        )
        bindAudioDataChannel(channel)
    }

    private fun bindAudioDataChannel(channel: DataChannel) {
        synchronized(lock) {
            audioDataChannel?.unregisterObserver()
            audioDataChannel?.close()
            audioDataChannel = channel
        }

        channel.registerObserver(object : DataChannel.Observer {
            override fun onBufferedAmountChange(previousAmount: Long) {
                val currentAmount = channel.bufferedAmount()
                val now = System.currentTimeMillis()
                if (currentAmount >= BUFFER_PRESSURE_WARNING_BYTES &&
                    now - lastBufferPressureLogAtMs >= BUFFER_PRESSURE_LOG_INTERVAL_MS
                ) {
                    lastBufferPressureLogAtMs = now
                    AppLogger.w(
                        "PeerConnection",
                        "data_channel_buffer_pressure",
                        "Data channel buffered amount is elevated",
                        context = mapOf(
                            "bufferedAmount" to currentAmount,
                            "previousAmount" to previousAmount
                        )
                    )
                }
            }

            override fun onStateChange() {
                AppLogger.i(
                    "PeerConnection",
                    "data_channel_state_change",
                    "Data channel state changed",
                    context = mapOf("state" to channel.state().name)
                )
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
                        AppLogger.i(
                            "PeerConnection",
                            "ice_connection_state_change",
                            "ICE connection state changed",
                            context = mapOf("state" to (state?.name ?: "null"))
                        )
                        when (state) {
                            PeerConnection.IceConnectionState.CONNECTED,
                            PeerConnection.IceConnectionState.COMPLETED -> {
                                updateDiagnostics(
                                    selectedCandidatePairType = "host-host",
                                    failureHint = ""
                                )
                                stateListener(AudioStreamState.STREAMING, null)
                            }

                            PeerConnection.IceConnectionState.DISCONNECTED -> {
                                updateDiagnostics(failureHint = "peer_disconnected")
                                stateListener(AudioStreamState.INTERRUPTED, "Peer disconnected")
                            }

                            PeerConnection.IceConnectionState.FAILED -> {
                                updateDiagnostics(
                                    failureHint = when (connectionDiagnostics.pathType) {
                                        NetworkPathType.USB_TETHER -> "usb_tether_check"
                                        NetworkPathType.WIFI_LAN -> "wifi_lan_check"
                                        NetworkPathType.UNKNOWN -> "network_interface_check"
                                    }
                                )
                                stateListener(AudioStreamState.FAILED, "ICE connection failed")
                            }

                            else -> Unit
                        }
                    }

                    override fun onIceConnectionReceivingChange(receiving: Boolean) = Unit
                    override fun onIceGatheringChange(state: PeerConnection.IceGatheringState?) {
                        AppLogger.d(
                            "PeerConnection",
                            "ice_gathering_state_change",
                            "ICE gathering state changed",
                            context = mapOf("state" to (state?.name ?: "null"))
                        )
                    }
                    override fun onIceCandidate(candidate: IceCandidate?) {
                        onLocalIceCandidate(candidate)
                    }
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
                        AppLogger.i(
                            "PeerConnection",
                            "remote_data_channel_open",
                            "Remote data channel opened",
                            context = mapOf("label" to dataChannel.label())
                        )
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
        AppLogger.d("PeerConnection", "ice_wait_start", "Waiting for ICE gathering completion")
        val deadlineMs = System.currentTimeMillis() + ICE_GATHER_TIMEOUT_MS
        while (System.currentTimeMillis() < deadlineMs) {
            if (peer.iceGatheringState() == PeerConnection.IceGatheringState.COMPLETE) {
                AppLogger.d("PeerConnection", "ice_wait_complete", "ICE gathering completed")
                return
            }
            Thread.sleep(30)
        }
        AppLogger.e("PeerConnection", "ice_wait_timeout", "ICE gathering timed out")
        error("ICE gathering did not complete in time")
    }

    private fun logDroppedFrame(reason: String, context: Map<String, Any?> = emptyMap()) {
        val now = System.currentTimeMillis()
        if (now - lastSendDropLogAtMs < SEND_DROP_LOG_INTERVAL_MS) {
            return
        }
        lastSendDropLogAtMs = now
        AppLogger.w(
            "PeerConnection",
            "pcm_send_dropped",
            "PCM frame send dropped",
            context = context + mapOf("reason" to reason)
        )
    }

    private fun extractFingerprint(sdp: String): String {
        return sdp.lineSequence()
            .firstOrNull { it.startsWith("a=fingerprint:") }
            ?.removePrefix("a=fingerprint:")
            ?.trim()
            ?: "unknown"
    }

    private fun resetDiagnostics() {
        connectionDiagnostics = ConnectionDiagnostics(
            pathType = NetworkPathClassifier.classifyFromLocalInterfaces()
        )
        diagnosticsListener(connectionDiagnostics)
    }

    private fun onLocalIceCandidate(candidate: IceCandidate?) {
        if (candidate == null) {
            return
        }
        val candidateType = candidate.sdp
            .split(' ')
            .zipWithNext()
            .firstOrNull { it.first == "typ" }
            ?.second
            .orEmpty()
        if (candidateType != "host") {
            return
        }

        val detectedPath = NetworkPathClassifier.classifyFromCandidateSdp(candidate.sdp)
        val nextPath = chooseMoreSpecificPath(connectionDiagnostics.pathType, detectedPath)
        updateDiagnostics(
            localCandidatesCount = connectionDiagnostics.localCandidatesCount + 1,
            pathType = nextPath
        )
        AppLogger.d(
            "PeerConnection",
            "ice_candidate_host_detected",
            "Detected host ICE candidate",
            context = mapOf(
                "pathType" to nextPath.name,
                "localCandidatesCount" to connectionDiagnostics.localCandidatesCount
            )
        )
    }

    private fun chooseMoreSpecificPath(current: NetworkPathType, incoming: NetworkPathType): NetworkPathType {
        if (incoming == NetworkPathType.UNKNOWN) {
            return current
        }
        if (current == NetworkPathType.USB_TETHER) {
            return current
        }
        return incoming
    }

    private fun updateDiagnostics(
        pathType: NetworkPathType? = null,
        localCandidatesCount: Int? = null,
        selectedCandidatePairType: String? = null,
        failureHint: String? = null
    ) {
        connectionDiagnostics = connectionDiagnostics.copy(
            pathType = pathType ?: connectionDiagnostics.pathType,
            localCandidatesCount = localCandidatesCount ?: connectionDiagnostics.localCandidatesCount,
            selectedCandidatePairType = selectedCandidatePairType ?: connectionDiagnostics.selectedCandidatePairType,
            failureHint = failureHint ?: connectionDiagnostics.failureHint
        )
        diagnosticsListener(connectionDiagnostics)
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
        private const val BUFFER_PRESSURE_WARNING_BYTES = 128_000L
        private const val SEND_DROP_LOG_INTERVAL_MS = 1_000L
        private const val BUFFER_PRESSURE_LOG_INTERVAL_MS = 1_000L
    }
}
