import Foundation
import UIKit

@MainActor
final class AppViewModel: ObservableObject {
    enum SetupStep {
        case entry
        case senderShowInit
        case senderVerifyCode
        case listenerScanInit
        case listenerShowConfirm
    }

    @Published var streamState: AudioStreamState = .idle
    @Published var statusMessage: String = L10n.tr("status.ready")
    @Published var setupStep: SetupStep = .entry
    @Published var initPayloadRaw: String = ""
    @Published var confirmPayloadRaw: String = ""
    @Published var verificationCode: String = ""
    @Published var activeSessionId: String = ""

    let logStore: LogStore

    init(logStore: LogStore = LogStore()) {
        self.logStore = logStore
    }

    private lazy var sessionController: WebRTCSessionController = {
        WebRTCSessionController(
            stateHandler: { [weak self] state, message in
                DispatchQueue.main.async {
                    self?.log(
                        .info,
                        "AppViewModel",
                        "WebRTC state update",
                        metadata: [
                            "state": state.rawValue,
                            "message": message ?? ""
                        ]
                    )
                    self?.streamState = state
                    self?.statusMessage = message ?? self?.stateLabel(state) ?? L10n.tr("status.ready")
                }
            },
            pcmFrameHandler: { [weak self] frame in
                self?.pcmPlayer.enqueue(frame)
                DispatchQueue.main.async {
                    guard let self else { return }
                    if !self.didShowReceivingMessage {
                        self.didShowReceivingMessage = true
                        self.log(
                            .info,
                            "AppViewModel",
                            "First remote audio frame received",
                            metadata: [
                                "sampleRate": String(frame.sampleRate),
                                "channels": String(frame.channels),
                                "bitsPerSample": String(frame.bitsPerSample)
                            ]
                        )
                        self.statusMessage = L10n.tr("status.receiving_remote_audio")
                    }
                }
            },
            logHandler: { [weak self] level, category, message, metadata in
                self?.log(level, category, message, metadata: metadata)
            }
        )
    }()
    private lazy var replayKitController = ReplayKitAudioCaptureController { [weak self] level, category, message, metadata in
        self?.log(level, category, message, metadata: metadata)
    }
    private let pcmPlayer = PcmPlayer()

    private var isConsumingReplayKitFrames = false
    private var didShowReceivingMessage = false
    private var localSenderFingerprint = ""
    private var pendingAnswerSdp = ""

    func beginListenerFlow() {
        log(.info, "AppViewModel", "Listener flow selected")
        setupStep = .listenerScanInit
        initPayloadRaw = ""
        confirmPayloadRaw = ""
        verificationCode = ""
        activeSessionId = ""
        pendingAnswerSdp = ""
        statusMessage = L10n.tr("status.listener_ready_to_scan")
    }

    func startSenderFlow() {
        log(.info, "AppViewModel", "Start sender flow requested")
        replayKitController.refreshBroadcastState()
        guard replayKitController.isBroadcastActive else {
            streamState = .failed
            statusMessage = L10n.tr("status.start_broadcast_first")
            log(.warning, "AppViewModel", "ReplayKit broadcast is not active")
            return
        }

        if !isConsumingReplayKitFrames {
            log(.info, "AppViewModel", "Start consuming ReplayKit frames")
            replayKitController.startConsumingFrames { [weak self] frame in
                _ = self?.sessionController.sendPcmFrame(frame)
            }
            isConsumingReplayKitFrames = true
        }
        streamState = .capturing
        setupStep = .senderShowInit
        statusMessage = L10n.tr("status.capturing_replaykit_audio")

        sessionController.createOffer { [weak self] result in
            guard let self else { return }
            DispatchQueue.main.async {
                switch result {
                case .success(let local):
                    let sessionId = UUID().uuidString
                    let payload = PairingInitPayload(
                        sessionId: sessionId,
                        senderDeviceName: UIDevice.current.name,
                        senderPubKeyFingerprint: local.fingerprint,
                        offerSdp: local.sdp,
                        expiresAtUnixMs: Self.nowMs() + Self.payloadTTLms
                    )
                    do {
                        self.initPayloadRaw = try QrPayloadCodec.encodeInit(payload)
                        self.localSenderFingerprint = local.fingerprint
                        self.activeSessionId = sessionId
                        self.statusMessage = L10n.tr("status.init_generated")
                        self.log(
                            .info,
                            "AppViewModel",
                            "Init payload generated",
                            metadata: [
                                "sessionId": sessionId,
                                "offerLength": String(payload.offerSdp.count),
                                "fingerprintHead": String(local.fingerprint.prefix(16))
                            ]
                        )
                    } catch {
                        self.resetToEntry(status: L10n.tr("error.failed_encode_offer"), asFailure: true)
                    }
                case .failure(let error):
                    self.resetToEntry(
                        status: self.message(from: error, fallbackKey: "error.webrtc_negotiation_failed"),
                        asFailure: true
                    )
                }
            }
        }
    }

    func createConfirm(from initRaw: String) {
        beginListenerFlow()
        log(
            .info,
            "AppViewModel",
            "Create confirm requested",
            metadata: ["initLength": String(initRaw.count)]
        )
        do {
            let payload = try QrPayloadCodec.decodeInit(initRaw)
            try PairingPayloadValidator.validateInit(payload, nowUnixMs: Self.nowMs())

            sessionController.createAnswer(for: payload.offerSdp) { [weak self] result in
                guard let self else { return }
                DispatchQueue.main.async {
                    switch result {
                    case .success(let local):
                        let confirmPayload = PairingConfirmPayload(
                            sessionId: payload.sessionId,
                            receiverDeviceName: UIDevice.current.name,
                            receiverPubKeyFingerprint: local.fingerprint,
                            answerSdp: local.sdp,
                            expiresAtUnixMs: Self.nowMs() + Self.payloadTTLms
                        )
                        do {
                            self.confirmPayloadRaw = try QrPayloadCodec.encodeConfirm(confirmPayload)
                            self.activeSessionId = payload.sessionId
                            self.setupStep = .listenerShowConfirm
                            self.verificationCode = VerificationCode.fromSessionAndFingerprints(
                                sessionId: payload.sessionId,
                                senderFingerprint: payload.senderPubKeyFingerprint,
                                receiverFingerprint: local.fingerprint
                            )
                            self.statusMessage = L10n.tr("status.confirm_generated")
                            self.log(
                                .info,
                                "AppViewModel",
                                "Confirm payload generated",
                                metadata: [
                                    "sessionId": payload.sessionId,
                                    "answerLength": String(confirmPayload.answerSdp.count),
                                    "fingerprintHead": String(local.fingerprint.prefix(16))
                                ]
                            )
                        } catch {
                            self.resetToEntry(status: L10n.tr("error.failed_encode_answer"), asFailure: true)
                        }
                    case .failure(let error):
                        self.resetToEntry(
                            status: self.message(from: error, fallbackKey: "error.webrtc_negotiation_failed"),
                            asFailure: true
                        )
                    }
                }
            }
        } catch {
            resetToEntry(status: message(from: error, fallbackKey: "error.invalid_init_payload"), asFailure: true)
        }
    }

    func prepareConfirmForVerification(from confirmRaw: String) {
        log(
            .info,
            "AppViewModel",
            "Prepare confirm for verification",
            metadata: [
                "confirmLength": String(confirmRaw.count),
                "sessionId": activeSessionId
            ]
        )
        do {
            let payload = try QrPayloadCodec.decodeConfirm(confirmRaw)
            try PairingPayloadValidator.validateConfirm(
                payload,
                expectedSessionId: activeSessionId,
                nowUnixMs: Self.nowMs()
            )
            guard !localSenderFingerprint.isEmpty else {
                resetToEntry(status: L10n.tr("error.invalid_confirm_payload"), asFailure: true)
                return
            }
            verificationCode = VerificationCode.fromSessionAndFingerprints(
                sessionId: activeSessionId,
                senderFingerprint: localSenderFingerprint,
                receiverFingerprint: payload.receiverPubKeyFingerprint
            )
            pendingAnswerSdp = payload.answerSdp
            setupStep = .senderVerifyCode
            statusMessage = L10n.tr("status.verification_ready")
        } catch {
            resetToEntry(status: message(from: error, fallbackKey: "error.invalid_confirm_payload"), asFailure: true)
        }
    }

    func approveVerificationAndConnect() {
        guard !pendingAnswerSdp.isEmpty else {
            resetToEntry(status: L10n.tr("error.invalid_confirm_payload"), asFailure: true)
            return
        }
        sessionController.applyAnswer(pendingAnswerSdp) { [weak self] result in
            DispatchQueue.main.async {
                switch result {
                case .success:
                    self?.pendingAnswerSdp = ""
                    self?.setupStep = .senderShowInit
                    self?.statusMessage = L10n.tr("status.answer_applied")
                case .failure(let error):
                    self?.resetToEntry(
                        status: self?.message(from: error, fallbackKey: "error.apply_answer_failed")
                            ?? L10n.tr("error.apply_answer_failed"),
                        asFailure: true
                    )
                }
            }
        }
    }

    func rejectVerificationAndRestart() {
        resetToEntry(status: L10n.tr("status.verification_mismatch"), asFailure: true)
    }

    func endSession() {
        log(
            .info,
            "AppViewModel",
            "Ending session",
            metadata: ["sessionId": activeSessionId]
        )
        resetToEntry(status: L10n.tr("status.session_ended"), asFailure: false)
    }

    private func resetToEntry(status: String, asFailure: Bool) {
        sessionController.close()
        replayKitController.stopConsumingFrames()
        replayKitController.stopBroadcastFlag()
        pcmPlayer.stop()

        isConsumingReplayKitFrames = false
        didShowReceivingMessage = false
        pendingAnswerSdp = ""
        localSenderFingerprint = ""

        streamState = asFailure ? .failed : .idle
        statusMessage = status
        setupStep = .entry
        initPayloadRaw = ""
        confirmPayloadRaw = ""
        verificationCode = ""
        activeSessionId = ""
    }

    private static var payloadTTLms: Int64 { 60_000 }

    private static func nowMs() -> Int64 {
        Int64(Date().timeIntervalSince1970 * 1000)
    }

    private func log(
        _ level: AppLogLevel,
        _ category: String,
        _ message: String,
        metadata: [String: String] = [:]
    ) {
        Task { @MainActor [weak self] in
            self?.logStore.append(level: level, category: category, message: message, metadata: metadata)
        }
    }

    private func stateLabel(_ state: AudioStreamState) -> String {
        switch state {
        case .idle:
            return L10n.tr("stream_state.idle")
        case .capturing:
            return L10n.tr("stream_state.capturing")
        case .connecting:
            return L10n.tr("stream_state.connecting")
        case .streaming:
            return L10n.tr("stream_state.streaming")
        case .interrupted:
            return L10n.tr("stream_state.interrupted")
        case .failed:
            return L10n.tr("stream_state.failed")
        case .ended:
            return L10n.tr("stream_state.ended")
        }
    }

    private func message(from error: Error, fallbackKey: String) -> String {
        if let failure = error as? SessionFailure {
            return failure.message
        }
        return L10n.tr(fallbackKey)
    }
}
