import Foundation
import WebRTC

final class WebRTCSessionController: NSObject {
    typealias StateHandler = (AudioStreamState, String?) -> Void

    private let factory: RTCPeerConnectionFactory
    private var peerConnection: RTCPeerConnection?
    private var iceCompletion: (() -> Void)?
    private let stateHandler: StateHandler
    private let pcmFrameHandler: (PcmFrame) -> Void

    private let syncLock = NSLock()
    private var audioDataChannel: RTCDataChannel?

    init(
        stateHandler: @escaping StateHandler,
        pcmFrameHandler: @escaping (PcmFrame) -> Void
    ) {
        RTCInitializeSSL()
        let encoderFactory = RTCDefaultVideoEncoderFactory()
        let decoderFactory = RTCDefaultVideoDecoderFactory()
        self.factory = RTCPeerConnectionFactory(encoderFactory: encoderFactory, decoderFactory: decoderFactory)
        self.stateHandler = stateHandler
        self.pcmFrameHandler = pcmFrameHandler
        super.init()
    }

    func createOffer(completion: @escaping (Result<(sdp: String, fingerprint: String), Error>) -> Void) {
        stateHandler(.connecting, nil)
        let peer = makePeerConnection()
        self.peerConnection = peer
        createLocalDataChannel(for: peer)

        let constraints = RTCMediaConstraints(mandatoryConstraints: nil, optionalConstraints: nil)
        peer.offer(for: constraints) { [weak self] offer, error in
            guard let self else { return }
            if let error {
                completion(.failure(error))
                self.stateHandler(.failed, error.localizedDescription)
                return
            }
            guard let offer else {
                completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: "Offer is nil")))
                return
            }

            peer.setLocalDescription(offer) { [weak self] setError in
                guard let self else { return }
                if let setError {
                    completion(.failure(setError))
                    self.stateHandler(.failed, setError.localizedDescription)
                    return
                }
                self.waitIceComplete { [weak self] in
                    guard let self else { return }
                    guard let sdp = peer.localDescription?.sdp else {
                        completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: "Local offer missing")))
                        return
                    }
                    completion(.success((sdp: sdp, fingerprint: self.extractFingerprint(from: sdp))))
                }
            }
        }
    }

    func createAnswer(
        for offerSdp: String,
        completion: @escaping (Result<(sdp: String, fingerprint: String), Error>) -> Void
    ) {
        stateHandler(.connecting, nil)
        let peer = makePeerConnection()
        self.peerConnection = peer

        let remoteOffer = RTCSessionDescription(type: .offer, sdp: offerSdp)
        peer.setRemoteDescription(remoteOffer) { [weak self] setError in
            guard let self else { return }
            if let setError {
                completion(.failure(setError))
                self.stateHandler(.failed, setError.localizedDescription)
                return
            }

            let constraints = RTCMediaConstraints(mandatoryConstraints: nil, optionalConstraints: nil)
            peer.answer(for: constraints) { answer, answerError in
                if let answerError {
                    completion(.failure(answerError))
                    self.stateHandler(.failed, answerError.localizedDescription)
                    return
                }
                guard let answer else {
                    completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: "Answer is nil")))
                    return
                }

                peer.setLocalDescription(answer) { localError in
                    if let localError {
                        completion(.failure(localError))
                        self.stateHandler(.failed, localError.localizedDescription)
                        return
                    }
                    self.waitIceComplete {
                        guard let sdp = peer.localDescription?.sdp else {
                            completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: "Local answer missing")))
                            return
                        }
                        completion(.success((sdp: sdp, fingerprint: self.extractFingerprint(from: sdp))))
                    }
                }
            }
        }
    }

    func applyAnswer(_ answerSdp: String, completion: @escaping (Result<Void, Error>) -> Void) {
        guard let peerConnection else {
            completion(.failure(SessionFailure(code: .webrtcNegotiationFailed, message: "Peer connection unavailable")))
            return
        }
        let answer = RTCSessionDescription(type: .answer, sdp: answerSdp)
        peerConnection.setRemoteDescription(answer) { [weak self] error in
            if let error {
                completion(.failure(error))
                self?.stateHandler(.failed, error.localizedDescription)
                return
            }
            completion(.success(()))
        }
    }

    func sendPcmFrame(_ frame: PcmFrame) -> Bool {
        let packet = PcmPacketCodec.encode(frame)
        syncLock.lock()
        let channel = audioDataChannel
        syncLock.unlock()

        guard let channel else {
            return false
        }
        guard channel.readyState == .open else {
            return false
        }
        if channel.bufferedAmount > maxBufferedAmountBytes {
            return false
        }
        let buffer = RTCDataBuffer(data: packet, isBinary: true)
        return channel.sendData(buffer)
    }

    func close() {
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
        return factory.peerConnection(with: config, constraints: constraints, delegate: self)
    }

    private func createLocalDataChannel(for peer: RTCPeerConnection) {
        let config = RTCDataChannelConfiguration()
        config.isOrdered = true
        config.maxRetransmits = 0
        guard let channel = peer.dataChannel(forLabel: audioChannelLabel, configuration: config) else {
            stateHandler(.failed, "Failed to create audio data channel")
            return
        }
        bindDataChannel(channel)
    }

    private func bindDataChannel(_ dataChannel: RTCDataChannel) {
        syncLock.lock()
        audioDataChannel?.delegate = nil
        audioDataChannel = dataChannel
        syncLock.unlock()
        dataChannel.delegate = self
    }

    private func waitIceComplete(completion: @escaping () -> Void) {
        if peerConnection?.iceGatheringState == .complete {
            completion()
            return
        }
        iceCompletion = completion
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
}

extension WebRTCSessionController: RTCPeerConnectionDelegate {
    func peerConnection(_ peerConnection: RTCPeerConnection, didChange stateChanged: RTCSignalingState) {}

    func peerConnection(_ peerConnection: RTCPeerConnection, didAdd stream: RTCMediaStream) {}

    func peerConnection(_ peerConnection: RTCPeerConnection, didRemove stream: RTCMediaStream) {}

    func peerConnectionShouldNegotiate(_ peerConnection: RTCPeerConnection) {}

    func peerConnection(_ peerConnection: RTCPeerConnection, didChange newState: RTCIceConnectionState) {
        switch newState {
        case .connected, .completed:
            stateHandler(.streaming, nil)
        case .disconnected:
            stateHandler(.interrupted, "Peer disconnected")
        case .failed:
            stateHandler(.failed, "ICE connection failed")
        default:
            break
        }
    }

    func peerConnection(_ peerConnection: RTCPeerConnection, didChange newState: RTCIceGatheringState) {
        if newState == .complete {
            let completion = iceCompletion
            iceCompletion = nil
            completion?()
        }
    }

    func peerConnection(_ peerConnection: RTCPeerConnection, didGenerate candidate: RTCIceCandidate) {}

    func peerConnection(_ peerConnection: RTCPeerConnection, didRemove candidates: [RTCIceCandidate]) {}

    func peerConnection(_ peerConnection: RTCPeerConnection, didOpen dataChannel: RTCDataChannel) {
        if dataChannel.label == audioChannelLabel {
            bindDataChannel(dataChannel)
        }
    }

    func peerConnection(_ peerConnection: RTCPeerConnection, didChange stateChanged: RTCPeerConnectionState) {}
}

extension WebRTCSessionController: RTCDataChannelDelegate {
    func dataChannelDidChangeState(_ dataChannel: RTCDataChannel) {
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
