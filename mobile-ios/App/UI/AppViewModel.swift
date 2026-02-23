import Foundation
import UIKit

@MainActor
final class AppViewModel: ObservableObject {
    @Published var streamState: AudioStreamState = .idle
    @Published var statusMessage: String = "Ready"
    @Published var offerPayloadRaw: String = ""
    @Published var answerPayloadRaw: String = ""
    @Published var activeSessionId: String = ""

    private lazy var sessionController: WebRTCSessionController = {
        WebRTCSessionController(
            stateHandler: { [weak self] state, message in
                DispatchQueue.main.async {
                    self?.streamState = state
                    self?.statusMessage = message ?? state.rawValue
                }
            },
            pcmFrameHandler: { [weak self] frame in
                self?.pcmPlayer.enqueue(frame)
                DispatchQueue.main.async {
                    guard let self else { return }
                    if !self.didShowReceivingMessage {
                        self.didShowReceivingMessage = true
                        self.statusMessage = "Receiving remote audio"
                    }
                }
            }
        )
    }()
    private let replayKitController = ReplayKitAudioCaptureController()
    private let pcmPlayer = PcmPlayer()

    private var isConsumingReplayKitFrames = false
    private var didShowReceivingMessage = false

    func startSenderFlow() {
        replayKitController.refreshBroadcastState()
        guard replayKitController.isBroadcastActive else {
            streamState = .failed
            statusMessage = "Start screen broadcast first (ReplayKit app audio capture)."
            return
        }

        if !isConsumingReplayKitFrames {
            replayKitController.startConsumingFrames { [weak self] frame in
                _ = self?.sessionController.sendPcmFrame(frame)
            }
            isConsumingReplayKitFrames = true
        }
        streamState = .capturing
        statusMessage = "Capturing ReplayKit app audio"

        sessionController.createOffer { [weak self] result in
            guard let self else { return }
            DispatchQueue.main.async {
                switch result {
                case .success(let local):
                    let sessionId = UUID().uuidString
                    let payload = SessionOfferPayload(
                        sessionId: sessionId,
                        senderDeviceName: UIDevice.current.name,
                        senderPubKeyFingerprint: local.fingerprint,
                        offerSdp: local.sdp,
                        expiresAtUnixMs: Self.nowMs() + Self.payloadTTLms
                    )
                    do {
                        self.offerPayloadRaw = try QrPayloadCodec.encodeOffer(payload)
                        self.activeSessionId = sessionId
                        self.statusMessage = "Offer generated"
                    } catch {
                        self.streamState = .failed
                        self.statusMessage = "Failed to encode offer"
                    }
                case .failure(let error):
                    self.streamState = .failed
                    self.statusMessage = error.localizedDescription
                }
            }
        }
    }

    func createAnswer(from offerRaw: String) {
        do {
            let offer = try QrPayloadCodec.decodeOffer(offerRaw)
            try PairingPayloadValidator.validateOffer(offer, nowUnixMs: Self.nowMs())

            sessionController.createAnswer(for: offer.offerSdp) { [weak self] result in
                guard let self else { return }
                DispatchQueue.main.async {
                    switch result {
                    case .success(let local):
                        let payload = SessionAnswerPayload(
                            sessionId: offer.sessionId,
                            receiverDeviceName: UIDevice.current.name,
                            receiverPubKeyFingerprint: local.fingerprint,
                            answerSdp: local.sdp,
                            expiresAtUnixMs: Self.nowMs() + Self.payloadTTLms
                        )
                        do {
                            self.answerPayloadRaw = try QrPayloadCodec.encodeAnswer(payload)
                            self.activeSessionId = offer.sessionId
                            self.statusMessage = "Answer generated"
                        } catch {
                            self.streamState = .failed
                            self.statusMessage = "Failed to encode answer"
                        }
                    case .failure(let error):
                        self.streamState = .failed
                        self.statusMessage = error.localizedDescription
                    }
                }
            }
        } catch {
            streamState = .failed
            statusMessage = "Invalid offer payload"
        }
    }

    func applyAnswer(from answerRaw: String) {
        do {
            let answer = try QrPayloadCodec.decodeAnswer(answerRaw)
            try PairingPayloadValidator.validateAnswer(
                answer,
                expectedSessionId: activeSessionId,
                nowUnixMs: Self.nowMs()
            )
            sessionController.applyAnswer(answer.answerSdp) { [weak self] result in
                DispatchQueue.main.async {
                    switch result {
                    case .success:
                        self?.statusMessage = "Answer applied"
                    case .failure(let error):
                        self?.streamState = .failed
                        self?.statusMessage = error.localizedDescription
                    }
                }
            }
        } catch {
            streamState = .failed
            statusMessage = "Invalid answer payload"
        }
    }

    func endSession() {
        sessionController.close()
        replayKitController.stopConsumingFrames()
        replayKitController.stopBroadcastFlag()
        pcmPlayer.stop()

        isConsumingReplayKitFrames = false
        didShowReceivingMessage = false

        streamState = .ended
        statusMessage = "Session ended"
        offerPayloadRaw = ""
        answerPayloadRaw = ""
        activeSessionId = ""
    }

    private static var payloadTTLms: Int64 { 60_000 }

    private static func nowMs() -> Int64 {
        Int64(Date().timeIntervalSince1970 * 1000)
    }
}
