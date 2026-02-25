import Foundation
import WebRTC

final class WebRTCSessionController: NSObject {
    typealias StateHandler = (AudioStreamState, String?) -> Void
    typealias LogHandler = (AppLogLevel, String, String, [String: String]) -> Void
    private struct IceWaitCallbacks {
        let onSuccess: () -> Void
        let onTimeout: () -> Void
    }
    private enum PcmDropReason: Equatable {
        case noDataChannel
        case channelNotOpen(state: Int)
        case bufferedAmountExceeded
    }

    private let factory: RTCPeerConnectionFactory
    private var peerConnection: RTCPeerConnection?
    private var iceWaitCallbacks: IceWaitCallbacks?
    private var iceTimeoutWorkItem: DispatchWorkItem?
    private let stateHandler: StateHandler
    private let pcmFrameHandler: (PcmFrame) -> Void
    private let logHandler: LogHandler?

    private let syncLock = NSLock()
    private var audioDataChannel: RTCDataChannel?
    private var lastPcmDropReason: PcmDropReason?
    private var lastPcmDropLogAt = Date.distantPast

    init(
        stateHandler: @escaping StateHandler,
        pcmFrameHandler: @escaping (PcmFrame) -> Void,
        logHandler: LogHandler? = nil
    ) {
        RTCInitializeSSL()
        let encoderFactory = RTCDefaultVideoEncoderFactory()
        let decoderFactory = RTCDefaultVideoDecoderFactory()
        self.factory = RTCPeerConnectionFactory(encoderFactory: encoderFactory, decoderFactory: decoderFactory)
        self.stateHandler = stateHandler
        self.pcmFrameHandler = pcmFrameHandler
        self.logHandler = logHandler
        super.init()
    }

    func createOffer(completion: @escaping (Result<(sdp: String, fingerprint: String), Error>) -> Void) {
        log(.info, "WebRTC", "Create offer requested")
        stateHandler(.connecting, nil)
        cancelIceWait()
        let peer = makePeerConnection()
        self.peerConnection = peer
        createLocalDataChannel(for: peer)

        let constraints = RTCMediaConstraints(mandatoryConstraints: nil, optionalConstraints: nil)
        peer.offer(for: constraints) { [weak self] offer, error in
            guard let self else { return }
            if let error {
                completion(.failure(error))
                self.stateHandler(.failed, L10n.tr("error.webrtc_negotiation_failed"))
                self.log(.error, "WebRTC", "Create offer failed", metadata: ["reason": error.localizedDescription])
                return
            }
            guard let offer else {
                completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: L10n.tr("error.offer_nil"))))
                self.log(.error, "WebRTC", "Offer object is nil")
                return
            }

            peer.setLocalDescription(offer) { [weak self] setError in
                guard let self else { return }
                if let setError {
                    completion(.failure(setError))
                    self.stateHandler(.failed, L10n.tr("error.webrtc_negotiation_failed"))
                    self.log(.error, "WebRTC", "setLocalDescription for offer failed", metadata: ["reason": setError.localizedDescription])
                    return
                }
                self.waitIceComplete(onSuccess: { [weak self] in
                    guard let self else { return }
                    guard let sdp = peer.localDescription?.sdp else {
                        completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: L10n.tr("error.local_offer_missing"))))
                        self.log(.error, "WebRTC", "Local offer SDP missing after setLocalDescription")
                        return
                    }
                    self.log(
                        .info,
                        "WebRTC",
                        "Create offer succeeded",
                        metadata: [
                            "offerLength": String(sdp.count),
                            "fingerprintHead": String(self.extractFingerprint(from: sdp).prefix(16))
                        ]
                    )
                    completion(.success((sdp: sdp, fingerprint: self.extractFingerprint(from: sdp))))
                }, onTimeout: {
                    completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: L10n.tr("error.ice_gather_timeout"))))
                    self.log(.error, "WebRTC", "ICE gathering timed out while creating offer")
                })
            }
        }
    }

    func createAnswer(
        for offerSdp: String,
        completion: @escaping (Result<(sdp: String, fingerprint: String), Error>) -> Void
    ) {
        log(.info, "WebRTC", "Create answer requested", metadata: ["offerLength": String(offerSdp.count)])
        stateHandler(.connecting, nil)
        cancelIceWait()
        let peer = makePeerConnection()
        self.peerConnection = peer

        let remoteOffer = RTCSessionDescription(type: .offer, sdp: offerSdp)
        peer.setRemoteDescription(remoteOffer) { [weak self] setError in
            guard let self else { return }
            if let setError {
                completion(.failure(setError))
                self.stateHandler(.failed, L10n.tr("error.webrtc_negotiation_failed"))
                self.log(.error, "WebRTC", "setRemoteDescription for offer failed", metadata: ["reason": setError.localizedDescription])
                return
            }

            let constraints = RTCMediaConstraints(mandatoryConstraints: nil, optionalConstraints: nil)
            peer.answer(for: constraints) { answer, answerError in
                if let answerError {
                    completion(.failure(answerError))
                    self.stateHandler(.failed, L10n.tr("error.webrtc_negotiation_failed"))
                    self.log(.error, "WebRTC", "Answer creation failed", metadata: ["reason": answerError.localizedDescription])
                    return
                }
                guard let answer else {
                    completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: L10n.tr("error.answer_nil"))))
                    self.log(.error, "WebRTC", "Answer object is nil")
                    return
                }

                peer.setLocalDescription(answer) { localError in
                    if let localError {
                        completion(.failure(localError))
                        self.stateHandler(.failed, L10n.tr("error.webrtc_negotiation_failed"))
                        self.log(.error, "WebRTC", "setLocalDescription for answer failed", metadata: ["reason": localError.localizedDescription])
                        return
                    }
                    self.waitIceComplete(onSuccess: {
                        guard let sdp = peer.localDescription?.sdp else {
                            completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: L10n.tr("error.local_answer_missing"))))
                            self.log(.error, "WebRTC", "Local answer SDP missing after setLocalDescription")
                            return
                        }
                        self.log(
                            .info,
                            "WebRTC",
                            "Create answer succeeded",
                            metadata: [
                                "answerLength": String(sdp.count),
                                "fingerprintHead": String(self.extractFingerprint(from: sdp).prefix(16))
                            ]
                        )
                        completion(.success((sdp: sdp, fingerprint: self.extractFingerprint(from: sdp))))
                    }, onTimeout: {
                        completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: L10n.tr("error.ice_gather_timeout"))))
                        self.log(.error, "WebRTC", "ICE gathering timed out while creating answer")
                    })
                }
            }
        }
    }

    func applyAnswer(_ answerSdp: String, completion: @escaping (Result<Void, Error>) -> Void) {
        log(.info, "WebRTC", "Apply answer requested", metadata: ["answerLength": String(answerSdp.count)])
        guard let peerConnection else {
            completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: L10n.tr("error.peer_connection_unavailable"))))
            log(.error, "WebRTC", "Peer connection unavailable when applying answer")
            return
        }
        let answer = RTCSessionDescription(type: .answer, sdp: answerSdp)
        peerConnection.setRemoteDescription(answer) { [weak self] error in
            if let error {
                completion(.failure(error))
                self?.stateHandler(.failed, L10n.tr("error.apply_answer_failed"))
                self?.log(.error, "WebRTC", "setRemoteDescription for answer failed", metadata: ["reason": error.localizedDescription])
                return
            }
            self?.log(.info, "WebRTC", "Apply answer succeeded")
            completion(.success(()))
        }
    }

    func sendPcmFrame(_ frame: PcmFrame) -> Bool {
        let packet = PcmPacketCodec.encode(frame)
        syncLock.lock()
        let channel = audioDataChannel
        syncLock.unlock()

        guard let channel else {
            logPcmDropIfNeeded(
                reason: .noDataChannel,
                message: "PCM frame dropped: no data channel"
            )
            return false
        }
        guard channel.readyState == .open else {
            logPcmDropIfNeeded(
                reason: .channelNotOpen(state: channel.readyState.rawValue),
                message: "PCM frame dropped: channel not open",
                metadata: ["state": String(channel.readyState.rawValue)]
            )
            return false
        }
        if channel.bufferedAmount > maxBufferedAmountBytes {
            logPcmDropIfNeeded(
                reason: .bufferedAmountExceeded,
                message: "PCM frame dropped: buffered amount exceeded",
                metadata: ["bufferedAmount": String(channel.bufferedAmount)]
            )
            return false
        }
        let buffer = RTCDataBuffer(data: packet, isBinary: true)
        return channel.sendData(buffer)
    }

    func close() {
        log(.info, "WebRTC", "Closing peer connection")
        cancelIceWait()
        syncLock.lock()
        audioDataChannel?.delegate = nil
        audioDataChannel?.close()
        audioDataChannel = nil
        syncLock.unlock()

        peerConnection?.close()
        peerConnection = nil
        stateHandler(.ended, nil)
    }

    private func makePeerConnection() -> RTCPeerConnection {
        let config = RTCConfiguration()
        config.iceServers = []
        config.sdpSemantics = .unifiedPlan
        config.continualGatheringPolicy = .gatherOnce
        config.tcpCandidatePolicy = .disabled

        let constraints = RTCMediaConstraints(mandatoryConstraints: nil, optionalConstraints: nil)
        guard let peer = factory.peerConnection(with: config, constraints: constraints, delegate: self) else {
            fatalError("Failed to create RTCPeerConnection")
        }
        log(.debug, "WebRTC", "Peer connection created")
        return peer
    }

    private func createLocalDataChannel(for peer: RTCPeerConnection) {
        let config = RTCDataChannelConfiguration()
        config.isOrdered = true
        config.maxRetransmits = 0
        guard let channel = peer.dataChannel(forLabel: audioChannelLabel, configuration: config) else {
            stateHandler(.failed, L10n.tr("error.data_channel_create_failed"))
            log(.error, "WebRTC", "Failed to create local data channel")
            return
        }
        log(.info, "WebRTC", "Local data channel created", metadata: ["label": audioChannelLabel])
        bindDataChannel(channel)
    }

    private func bindDataChannel(_ dataChannel: RTCDataChannel) {
        syncLock.lock()
        audioDataChannel?.delegate = nil
        audioDataChannel = dataChannel
        syncLock.unlock()
        dataChannel.delegate = self
    }

    private func waitIceComplete(onSuccess: @escaping () -> Void, onTimeout: @escaping () -> Void) {
        if peerConnection?.iceGatheringState == .complete {
            log(.debug, "WebRTC", "ICE already complete")
            onSuccess()
            return
        }

        syncLock.lock()
        iceWaitCallbacks = IceWaitCallbacks(onSuccess: onSuccess, onTimeout: onTimeout)
        iceTimeoutWorkItem?.cancel()
        let timeoutWorkItem = DispatchWorkItem { [weak self] in
            self?.finishIceWait(timedOut: true)
        }
        iceTimeoutWorkItem = timeoutWorkItem
        syncLock.unlock()

        DispatchQueue.global().asyncAfter(
            deadline: .now() + iceGatherTimeoutSeconds,
            execute: timeoutWorkItem
        )
        log(.debug, "WebRTC", "Waiting for ICE completion", metadata: ["timeoutSec": String(iceGatherTimeoutSeconds)])
    }

    private func finishIceWait(timedOut: Bool) {
        syncLock.lock()
        let callbacks = iceWaitCallbacks
        iceWaitCallbacks = nil
        let timeoutWorkItem = iceTimeoutWorkItem
        iceTimeoutWorkItem = nil
        syncLock.unlock()

        timeoutWorkItem?.cancel()
        guard let callbacks else {
            return
        }
        if timedOut {
            stateHandler(.failed, L10n.tr("error.ice_gather_timeout"))
            log(.error, "WebRTC", "ICE wait timed out")
            callbacks.onTimeout()
        } else {
            log(.debug, "WebRTC", "ICE wait completed")
            callbacks.onSuccess()
        }
    }

    private func cancelIceWait() {
        syncLock.lock()
        iceWaitCallbacks = nil
        let timeoutWorkItem = iceTimeoutWorkItem
        iceTimeoutWorkItem = nil
        syncLock.unlock()
        timeoutWorkItem?.cancel()
    }

    private func extractFingerprint(from sdp: String) -> String {
        let line = sdp
            .split(separator: "\n")
            .first { $0.starts(with: "a=fingerprint:") }
        return line?.replacingOccurrences(of: "a=fingerprint:", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
            ?? "unknown"
    }

    private let audioChannelLabel = "audio-pcm"
    private let maxBufferedAmountBytes: UInt64 = 256_000
    private let iceGatherTimeoutSeconds: TimeInterval = 8
    private let pcmDropLogThrottleSeconds: TimeInterval = 2

    private func log(
        _ level: AppLogLevel,
        _ category: String,
        _ message: String,
        metadata: [String: String] = [:]
    ) {
        logHandler?(level, category, message, metadata)
    }

    private func logPcmDropIfNeeded(
        reason: PcmDropReason,
        message: String,
        metadata: [String: String] = [:]
    ) {
        let now = Date()
        syncLock.lock()
        let reasonChanged = reason != lastPcmDropReason
        let intervalElapsed = now.timeIntervalSince(lastPcmDropLogAt) >= pcmDropLogThrottleSeconds
        let shouldLog = reasonChanged || intervalElapsed
        if shouldLog {
            lastPcmDropReason = reason
            lastPcmDropLogAt = now
        }
        syncLock.unlock()

        if shouldLog {
            log(.warning, "WebRTC", message, metadata: metadata)
        }
    }
}

extension WebRTCSessionController: RTCPeerConnectionDelegate {
    func peerConnection(_ peerConnection: RTCPeerConnection, didChange stateChanged: RTCSignalingState) {}

    func peerConnection(_ peerConnection: RTCPeerConnection, didAdd stream: RTCMediaStream) {}

    func peerConnection(_ peerConnection: RTCPeerConnection, didRemove stream: RTCMediaStream) {}

    func peerConnectionShouldNegotiate(_ peerConnection: RTCPeerConnection) {}

    func peerConnection(_ peerConnection: RTCPeerConnection, didChange newState: RTCIceConnectionState) {
        log(.info, "WebRTC", "ICE connection state changed", metadata: ["state": String(newState.rawValue)])
        switch newState {
        case .connected, .completed:
            stateHandler(.streaming, nil)
        case .disconnected:
            stateHandler(.interrupted, L10n.tr("status.peer_disconnected"))
        case .failed:
            stateHandler(.failed, L10n.tr("status.ice_connection_failed"))
        default:
            break
        }
    }

    func peerConnection(_ peerConnection: RTCPeerConnection, didChange newState: RTCIceGatheringState) {
        log(.debug, "WebRTC", "ICE gathering state changed", metadata: ["state": String(newState.rawValue)])
        if newState == .complete {
            finishIceWait(timedOut: false)
        }
    }

    func peerConnection(_ peerConnection: RTCPeerConnection, didGenerate candidate: RTCIceCandidate) {}

    func peerConnection(_ peerConnection: RTCPeerConnection, didRemove candidates: [RTCIceCandidate]) {}

    func peerConnection(_ peerConnection: RTCPeerConnection, didOpen dataChannel: RTCDataChannel) {
        log(.info, "WebRTC", "Remote data channel opened", metadata: ["label": dataChannel.label])
        if dataChannel.label == audioChannelLabel {
            bindDataChannel(dataChannel)
        }
    }

    func peerConnection(_ peerConnection: RTCPeerConnection, didChange stateChanged: RTCPeerConnectionState) {}
}

extension WebRTCSessionController: RTCDataChannelDelegate {
    func dataChannelDidChangeState(_ dataChannel: RTCDataChannel) {
        log(.info, "WebRTC", "Data channel state changed", metadata: ["state": String(dataChannel.readyState.rawValue)])
        if dataChannel.readyState == .open {
            stateHandler(.streaming, nil)
        }
    }

    func dataChannel(_ dataChannel: RTCDataChannel, didReceiveMessageWith buffer: RTCDataBuffer) {
        guard buffer.isBinary else {
            return
        }
        guard let frame = PcmPacketCodec.decode(buffer.data) else {
            return
        }
        pcmFrameHandler(frame)
    }
}
