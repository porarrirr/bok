import AVFoundation
import Foundation

final class PcmPlayer {
    private let engine = AVAudioEngine()
    private let playerNode = AVAudioPlayerNode()
    private let queue = DispatchQueue(label: "com.example.p2paudio.pcm-player")

    private var currentFormatKey: String?
    private var currentFormat: AVAudioFormat?

    init() {
        engine.attach(playerNode)
    }

    func enqueue(_ frame: PcmFrame) {
        queue.async { [weak self] in
            self?.enqueueOnQueue(frame)
        }
    }

    func stop() {
        queue.async { [weak self] in
            self?.playerNode.stop()
            self?.engine.stop()
            self?.engine.reset()
            self?.currentFormat = nil
            self?.currentFormatKey = nil
        }
    }

    private func enqueueOnQueue(_ frame: PcmFrame) {
        guard let format = prepareFormatIfNeeded(for: frame) else {
            return
        }
        guard let buffer = makeBuffer(frame: frame, format: format) else {
            return
        }

        playerNode.scheduleBuffer(buffer, completionHandler: nil)
        if !playerNode.isPlaying {
            playerNode.play()
        }
    }

    private func prepareFormatIfNeeded(for frame: PcmFrame) -> AVAudioFormat? {
        let key = "\(frame.sampleRate)-\(frame.channels)-\(frame.bitsPerSample)"
        if currentFormatKey != key {
            playerNode.stop()
            engine.stop()
            engine.reset()

            guard let newFormat = AVAudioFormat(
                commonFormat: .pcmFormatInt16,
                sampleRate: Double(frame.sampleRate),
                channels: AVAudioChannelCount(frame.channels),
                interleaved: true
            ) else {
                return nil
            }

            engine.connect(playerNode, to: engine.mainMixerNode, format: newFormat)

            do {
                try engine.start()
            } catch {
                return nil
            }

            currentFormat = newFormat
            currentFormatKey = key
            return newFormat
        }

        if !engine.isRunning {
            do {
                try engine.start()
            } catch {
                return nil
            }
        }
        return currentFormat
    }

    private func makeBuffer(frame: PcmFrame, format: AVAudioFormat) -> AVAudioPCMBuffer? {
        let frameCount = AVAudioFrameCount(frame.frameSamplesPerChannel)
        guard let pcmBuffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: frameCount) else {
            return nil
        }
        pcmBuffer.frameLength = frameCount

        let bufferList = UnsafeMutableAudioBufferListPointer(pcmBuffer.mutableAudioBufferList)
        guard let audioBuffer = bufferList.first, let dst = audioBuffer.mData else {
            return nil
        }

        let copySize = min(Int(audioBuffer.mDataByteSize), frame.pcmData.count)
        frame.pcmData.withUnsafeBytes { src in
            guard let srcBase = src.baseAddress else { return }
            memcpy(dst, srcBase, copySize)
            if copySize < Int(audioBuffer.mDataByteSize) {
                memset(dst.advanced(by: copySize), 0, Int(audioBuffer.mDataByteSize) - copySize)
            }
        }

        return pcmBuffer
    }
}
