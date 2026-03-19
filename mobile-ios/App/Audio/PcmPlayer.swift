import AVFoundation
import Foundation

struct PcmPlayerOverflowTrimResult {
    let droppedFrameCount: Int
    let nextExpectedSequence: Int?
}

func trimOverflowFramesForRealtimePlayback(
    pendingSequenceNumbers: inout [Int],
    scheduledFrames: Int,
    maxQueueFrames: Int,
    expectedSequence: Int?
) -> PcmPlayerOverflowTrimResult {
    var nextExpectedSequence = expectedSequence
    var droppedFrameCount = 0

    while pendingSequenceNumbers.count + scheduledFrames > maxQueueFrames, !pendingSequenceNumbers.isEmpty {
        let droppedSequence = pendingSequenceNumbers.removeFirst()
        droppedFrameCount += 1
        if let expectedSequence = nextExpectedSequence, droppedSequence >= expectedSequence {
            nextExpectedSequence = droppedSequence + 1
        }
    }

    return PcmPlayerOverflowTrimResult(
        droppedFrameCount: droppedFrameCount,
        nextExpectedSequence: nextExpectedSequence
    )
}

func playbackStartThresholdFrames(startupPrebufferFrames: Int, minTrackBufferFrames: Int) -> Int {
    max(startupPrebufferFrames, minTrackBufferFrames)
}

func playbackResumeThresholdFrames(steadyPrebufferFrames: Int, minTrackBufferFrames: Int) -> Int {
    max(steadyPrebufferFrames, max(minTrackBufferFrames / 2, 1))
}

private struct QueuedPcmFrame {
    let frame: PcmFrame
    let arrivalRealtimeMs: UInt64
}

final class PcmPlayer {
    typealias DiagnosticsHandler = (AudioStreamDiagnostics) -> Void
    typealias ErrorHandler = (SessionFailure) -> Void
    typealias LogHandler = (AppLogLevel, String, String, [String: String]) -> Void

    private let source: AudioStreamSource
    private let startupPrebufferFrames: Int
    private let steadyPrebufferFrames: Int
    private let maxQueueFrames: Int
    private let minTrackBufferFrames: Int
    private let diagnosticsHandler: DiagnosticsHandler
    private let errorHandler: ErrorHandler?
    private let logHandler: LogHandler?
    private let engine = AVAudioEngine()
    private let playerNode = AVAudioPlayerNode()
    private let queue = DispatchQueue(label: "com.example.p2paudio.pcm-player")

    private var currentFormatKey: String?
    private var currentFormat: AVAudioFormat?
    private var pendingFrames: [QueuedPcmFrame] = []
    private var scheduledFrames = 0
    private var playbackStarted = false
    private var expectedSequence: Int?
    private var generation = 0
    private var playedFrames: Int64 = 0
    private var decodedPackets: Int64 = 0
    private var staleFrameDrops: Int64 = 0
    private var queueOverflowDrops: Int64 = 0
    private var currentSampleRate = 0
    private var currentChannels = 0
    private var currentBitsPerSample = 0
    private var currentFrameSamplesPerChannel = 0

    init(
        source: AudioStreamSource,
        startupPrebufferFrames: Int = 3,
        steadyPrebufferFrames: Int = 3,
        maxQueueFrames: Int = 20,
        minTrackBufferFrames: Int = 8,
        diagnosticsHandler: @escaping DiagnosticsHandler = { _ in },
        errorHandler: ErrorHandler? = nil,
        logHandler: LogHandler? = nil
    ) {
        self.source = source
        self.startupPrebufferFrames = max(startupPrebufferFrames, 1)
        self.steadyPrebufferFrames = max(steadyPrebufferFrames, 1)
        self.maxQueueFrames = max(maxQueueFrames, self.startupPrebufferFrames)
        self.minTrackBufferFrames = max(minTrackBufferFrames, 1)
        self.diagnosticsHandler = diagnosticsHandler
        self.errorHandler = errorHandler
        self.logHandler = logHandler
        engine.attach(playerNode)
    }

    func enqueue(_ frame: PcmFrame, arrivalRealtimeMs: UInt64 = realtimeNowMs()) {
        queue.async { [weak self] in
            self?.enqueueOnQueue(frame, arrivalRealtimeMs: arrivalRealtimeMs)
        }
    }

    func stop() {
        queue.async { [weak self] in
            self?.resetUnsafe(notifyDiagnostics: true)
        }
    }

    private func enqueueOnQueue(_ frame: PcmFrame, arrivalRealtimeMs: UInt64) {
        guard let format = prepareFormatIfNeeded(for: frame) else {
            return
        }
        decodedPackets += 1
        currentSampleRate = frame.sampleRate
        currentChannels = frame.channels
        currentBitsPerSample = frame.bitsPerSample
        currentFrameSamplesPerChannel = frame.frameSamplesPerChannel

        if let expectedSequence, frame.sequence < expectedSequence {
            staleFrameDrops += 1
            publishDiagnosticsUnsafe()
            return
        }
        if pendingFrames.contains(where: { $0.frame.sequence == frame.sequence }) {
            return
        }

        pendingFrames.append(
            QueuedPcmFrame(
                frame: frame,
                arrivalRealtimeMs: arrivalRealtimeMs
            )
        )
        pendingFrames.sort {
            if $0.frame.sequence == $1.frame.sequence {
                return $0.arrivalRealtimeMs < $1.arrivalRealtimeMs
            }
            return $0.frame.sequence < $1.frame.sequence
        }

        var pendingSequenceNumbers = pendingFrames.map(\.frame.sequence)
        let trimResult = trimOverflowFramesForRealtimePlayback(
            pendingSequenceNumbers: &pendingSequenceNumbers,
            scheduledFrames: scheduledFrames,
            maxQueueFrames: maxQueueFrames,
            expectedSequence: expectedSequence
        )
        if trimResult.droppedFrameCount > 0 {
            queueOverflowDrops += Int64(trimResult.droppedFrameCount)
            self.expectedSequence = trimResult.nextExpectedSequence
            let retained = Set(pendingSequenceNumbers)
            pendingFrames.removeAll { !retained.contains($0.frame.sequence) }
            log(
                .warning,
                "Dropped queued PCM frames to cap playback latency",
                metadata: [
                    "source": source.rawValue,
                    "droppedFrames": String(trimResult.droppedFrameCount),
                    "scheduledFrames": String(scheduledFrames),
                    "queueDepth": String(pendingFrames.count)
                ]
            )
        }

        drainQueueUnsafe(format: format)
        publishDiagnosticsUnsafe()
    }

    private func prepareFormatIfNeeded(for frame: PcmFrame) -> AVAudioFormat? {
        let key = "\(frame.sampleRate)-\(frame.channels)-\(frame.bitsPerSample)"
        if currentFormatKey != key {
            resetUnsafe(notifyDiagnostics: false)

            guard let newFormat = AVAudioFormat(
                commonFormat: .pcmFormatFloat32,
                sampleRate: Double(frame.sampleRate),
                channels: AVAudioChannelCount(frame.channels),
                interleaved: false
            ) else {
                handleError(SessionFailure(code: .webrtcNegotiationFailed, message: L10n.tr("error.audio_playback_unavailable")))
                return nil
            }

            engine.disconnectNodeOutput(playerNode)
            engine.connect(playerNode, to: engine.mainMixerNode, format: newFormat)
            engine.prepare()

            currentFormat = newFormat
            currentFormatKey = key
            log(
                .info,
                "Prepared playback format",
                metadata: [
                    "source": source.rawValue,
                    "sampleRate": String(frame.sampleRate),
                    "channels": String(frame.channels),
                    "bitsPerSample": String(frame.bitsPerSample)
                ]
            )
            return newFormat
        }
        return currentFormat
    }

    private func makeBuffer(frame: PcmFrame, format: AVAudioFormat) -> AVAudioPCMBuffer? {
        let frameCount = AVAudioFrameCount(frame.frameSamplesPerChannel)
        guard let pcmBuffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: frameCount) else {
            return nil
        }
        pcmBuffer.frameLength = frameCount

        guard
            let channelData = pcmBuffer.floatChannelData,
            frame.channels > 0,
            frame.bitsPerSample == 16
        else {
            return nil
        }

        let sampleCount = frame.frameSamplesPerChannel * frame.channels
        let expectedBytes = sampleCount * MemoryLayout<Int16>.size
        guard frame.pcmData.count >= expectedBytes else {
            return nil
        }

        frame.pcmData.withUnsafeBytes { rawBuffer in
            guard let samples = rawBuffer.bindMemory(to: Int16.self).baseAddress else { return }
            for channel in 0..<frame.channels {
                let destination = channelData[channel]
                for frameIndex in 0..<frame.frameSamplesPerChannel {
                    let sampleIndex = (frameIndex * frame.channels) + channel
                    destination[frameIndex] = Float(samples[sampleIndex]) / Float(Int16.max)
                }
            }
        }

        return pcmBuffer
    }

    private func drainQueueUnsafe(format: AVAudioFormat) {
        while let next = nextFrameToScheduleUnsafe(), scheduledFrames < maxQueueFrames {
            guard let buffer = makeBuffer(frame: next.frame, format: format) else {
                continue
            }

            let scheduledGeneration = generation
            scheduledFrames += 1
            playerNode.scheduleBuffer(buffer, completionCallbackType: .dataPlayedBack) { [weak self] _ in
                self?.queue.async { [weak self] in
                    guard let self, self.generation == scheduledGeneration else {
                        return
                    }
                    self.scheduledFrames = max(self.scheduledFrames - 1, 0)
                    self.playedFrames += 1
                    if self.playedFrames == 1 {
                        self.log(
                            .info,
                            "Remote audio playback started",
                            metadata: [
                                "source": self.source.rawValue,
                                "scheduledFrames": String(self.scheduledFrames),
                                "decodedPackets": String(self.decodedPackets)
                            ]
                        )
                    }
                    self.publishDiagnosticsUnsafe()
                    if let format = self.currentFormat {
                        self.drainQueueUnsafe(format: format)
                    }
                }
            })
        }

        let startThresholdFrames = playbackStartThresholdFrames(
            startupPrebufferFrames: startupPrebufferFrames,
            minTrackBufferFrames: minTrackBufferFrames
        )
        let resumeThresholdFrames = playbackResumeThresholdFrames(
            steadyPrebufferFrames: steadyPrebufferFrames,
            minTrackBufferFrames: minTrackBufferFrames
        )

        if !playbackStarted && scheduledFrames >= startThresholdFrames {
            do {
                try ensureEngineRunningUnsafe()
                playerNode.play()
                playbackStarted = true
                log(
                    .info,
                    "Playback start threshold reached",
                    metadata: [
                        "source": source.rawValue,
                        "scheduledFrames": String(scheduledFrames),
                        "targetFrames": String(startThresholdFrames),
                        "queueDepth": String(pendingFrames.count + scheduledFrames)
                    ]
                )
            } catch {
                handleError(
                    SessionFailure(
                        code: .webrtcNegotiationFailed,
                        message: L10n.tr("error.audio_playback_unavailable")
                    )
                )
            }
        } else if playbackStarted && !playerNode.isPlaying && scheduledFrames >= resumeThresholdFrames {
            do {
                try ensureEngineRunningUnsafe()
                playerNode.play()
                log(
                    .info,
                    "Playback resumed after buffer refill",
                    metadata: [
                        "source": source.rawValue,
                        "scheduledFrames": String(scheduledFrames),
                        "targetFrames": String(resumeThresholdFrames)
                    ]
                )
            } catch {
                handleError(
                    SessionFailure(
                        code: .webrtcNegotiationFailed,
                        message: L10n.tr("error.audio_playback_unavailable")
                    )
                )
            }
        }
    }

    private func nextFrameToScheduleUnsafe() -> QueuedPcmFrame? {
        while let head = pendingFrames.first, let expectedSequence, head.frame.sequence < expectedSequence {
            pendingFrames.removeFirst()
            staleFrameDrops += 1
        }

        guard !pendingFrames.isEmpty else {
            return nil
        }

        let next = pendingFrames.removeFirst()
        if let expectedSequence {
            self.expectedSequence = max(expectedSequence, next.frame.sequence) + 1
        } else {
            self.expectedSequence = next.frame.sequence + 1
        }
        return next
    }

    private func ensureEngineRunningUnsafe() throws {
        try AudioPlaybackSession.shared.activateForPlayback()
        if !engine.isRunning {
            try engine.start()
            log(
                .info,
                "Audio engine started",
                metadata: [
                    "source": source.rawValue,
                    "playerIsPlaying": String(playerNode.isPlaying)
                ]
            )
        }
    }

    private func resetUnsafe(notifyDiagnostics: Bool) {
        generation &+= 1
        playerNode.stop()
        engine.stop()
        engine.reset()
        currentFormat = nil
        currentFormatKey = nil
        pendingFrames.removeAll()
        scheduledFrames = 0
        playbackStarted = false
        expectedSequence = nil
        playedFrames = 0
        decodedPackets = 0
        staleFrameDrops = 0
        queueOverflowDrops = 0
        currentSampleRate = 0
        currentChannels = 0
        currentBitsPerSample = 0
        currentFrameSamplesPerChannel = 0
        if notifyDiagnostics {
            diagnosticsHandler(AudioStreamDiagnostics())
        }
    }

    private func publishDiagnosticsUnsafe() {
        diagnosticsHandler(
            AudioStreamDiagnostics(
                source: source,
                sampleRate: currentSampleRate,
                channels: currentChannels,
                bitsPerSample: currentBitsPerSample,
                frameSamplesPerChannel: currentFrameSamplesPerChannel,
                frameDurationMs: currentSampleRate > 0
                    ? Int((Double(currentFrameSamplesPerChannel) / Double(currentSampleRate)) * 1000.0)
                    : 0,
                startupTargetFrames: startupPrebufferFrames,
                targetPrebufferFrames: playbackStarted
                    ? playbackResumeThresholdFrames(
                        steadyPrebufferFrames: steadyPrebufferFrames,
                        minTrackBufferFrames: minTrackBufferFrames
                    )
                    : playbackStartThresholdFrames(
                        startupPrebufferFrames: startupPrebufferFrames,
                        minTrackBufferFrames: minTrackBufferFrames
                    ),
                maxQueueFrames: maxQueueFrames,
                queueDepthFrames: pendingFrames.count + scheduledFrames,
                playedFrames: playedFrames,
                decodedPackets: decodedPackets,
                staleFrameDrops: staleFrameDrops,
                queueOverflowDrops: queueOverflowDrops
            )
        )
    }

    private func handleError(_ failure: SessionFailure) {
        log(
            .error,
            "Playback pipeline failed",
            metadata: [
                "source": source.rawValue,
                "failureCode": failure.code.rawValue,
                "reason": failure.message
            ]
        )
        errorHandler?(failure)
    }

    private func log(
        _ level: AppLogLevel,
        _ message: String,
        metadata: [String: String] = [:]
    ) {
        logHandler?(level, "PcmPlayer", message, metadata)
    }

    private static func realtimeNowMs() -> UInt64 {
        DispatchTime.now().uptimeNanoseconds / 1_000_000
    }
}
