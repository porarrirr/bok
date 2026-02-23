import AudioToolbox
import CoreMedia
import Foundation
import ReplayKit

final class SampleHandler: RPBroadcastSampleHandler {
    private let sharedDefaults = UserDefaults(suiteName: SampleHandler.appGroupId)
    private var sequence: Int32 = 0

    private lazy var bridgeFileURL: URL? = {
        guard let container = FileManager.default.containerURL(
            forSecurityApplicationGroupIdentifier: Self.appGroupId
        ) else {
            return nil
        }
        return container.appendingPathComponent(Self.bridgeFileName)
    }()

    override func broadcastStarted(withSetupInfo setupInfo: [String : NSObject]?) {
        sharedDefaults?.set(true, forKey: Self.broadcastActiveKey)
        sequence = 0
        resetBridgeFile()
    }

    override func broadcastPaused() {}

    override func broadcastResumed() {}

    override func broadcastFinished() {
        sharedDefaults?.set(false, forKey: Self.broadcastActiveKey)
    }

    override func processSampleBuffer(_ sampleBuffer: CMSampleBuffer, with sampleBufferType: RPSampleBufferType) {
        guard sampleBufferType == .audioApp else {
            return
        }
        guard let frame = makeFrame(from: sampleBuffer) else {
            return
        }

        let packet = BridgePcmPacketCodec.encode(frame)
        appendPacket(packet)
    }

    private func makeFrame(from sampleBuffer: CMSampleBuffer) -> BridgePcmFrame? {
        guard let formatDescription = CMSampleBufferGetFormatDescription(sampleBuffer),
              let asbdPointer = CMAudioFormatDescriptionGetStreamBasicDescription(formatDescription) else {
            return nil
        }

        let asbd = asbdPointer.pointee
        guard asbd.mFormatID == kAudioFormatLinearPCM else {
            return nil
        }

        let channels = Int(asbd.mChannelsPerFrame)
        let sampleRate = Int(asbd.mSampleRate.rounded())
        guard channels > 0, sampleRate > 0 else {
            return nil
        }

        guard let dataBuffer = CMSampleBufferGetDataBuffer(sampleBuffer) else {
            return nil
        }

        var totalLength = 0
        var dataPointer: UnsafeMutablePointer<Int8>?
        let status = CMBlockBufferGetDataPointer(
            dataBuffer,
            atOffset: 0,
            lengthAtOffsetOut: nil,
            totalLengthOut: &totalLength,
            dataPointerOut: &dataPointer
        )
        guard status == kCMBlockBufferNoErr,
              let dataPointer,
              totalLength > 0 else {
            return nil
        }

        let pcm16Data: Data
        let isFloat = (asbd.mFormatFlags & kAudioFormatFlagIsFloat) != 0
        if isFloat, asbd.mBitsPerChannel == 32 {
            pcm16Data = convertFloat32ToPcm16(
                UnsafeRawPointer(dataPointer),
                byteCount: totalLength
            )
        } else if asbd.mBitsPerChannel == 16 {
            pcm16Data = Data(bytes: dataPointer, count: totalLength)
        } else {
            return nil
        }

        let frameSamplesPerChannel = pcm16Data.count / max(1, channels * 2)
        guard frameSamplesPerChannel > 0 else {
            return nil
        }

        defer { sequence &+= 1 }
        return BridgePcmFrame(
            sequence: Int(sequence),
            timestampMs: Int64(Date().timeIntervalSince1970 * 1000),
            sampleRate: sampleRate,
            channels: channels,
            bitsPerSample: 16,
            frameSamplesPerChannel: frameSamplesPerChannel,
            pcmData: pcm16Data
        )
    }

    private func convertFloat32ToPcm16(_ rawPointer: UnsafeRawPointer, byteCount: Int) -> Data {
        let sampleCount = byteCount / MemoryLayout<Float>.size
        let floatPointer = rawPointer.bindMemory(to: Float.self, capacity: sampleCount)
        var output = Data(count: sampleCount * MemoryLayout<Int16>.size)

        output.withUnsafeMutableBytes { outBytes in
            guard let outBase = outBytes.bindMemory(to: Int16.self).baseAddress else {
                return
            }
            for index in 0..<sampleCount {
                let clamped = max(-1.0, min(1.0, floatPointer[index]))
                outBase[index] = Int16(clamped * Float(Int16.max))
            }
        }

        return output
    }

    private func appendPacket(_ packet: Data) {
        guard let bridgeFileURL else {
            return
        }

        if shouldResetBridgeFile(url: bridgeFileURL) {
            resetBridgeFile()
        }

        if !FileManager.default.fileExists(atPath: bridgeFileURL.path) {
            FileManager.default.createFile(atPath: bridgeFileURL.path, contents: nil)
        }

        guard let handle = try? FileHandle(forWritingTo: bridgeFileURL) else {
            return
        }

        var length = UInt32(packet.count).littleEndian
        var record = Data(bytes: &length, count: MemoryLayout<UInt32>.size)
        record.append(packet)

        handle.seekToEndOfFile()
        handle.write(record)
        handle.closeFile()
    }

    private func shouldResetBridgeFile(url: URL) -> Bool {
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: url.path),
              let size = attributes[.size] as? NSNumber else {
            return false
        }
        return size.intValue >= Self.maxBridgeBytes
    }

    private func resetBridgeFile() {
        guard let bridgeFileURL else {
            return
        }
        try? FileManager.default.removeItem(at: bridgeFileURL)
        FileManager.default.createFile(atPath: bridgeFileURL.path, contents: nil)
    }

    private static let appGroupId = "group.com.example.p2paudio"
    private static let broadcastActiveKey = "broadcast_active"
    private static let bridgeFileName = "replaykit_pcm_bridge.bin"
    private static let maxBridgeBytes = 2 * 1024 * 1024
}

private struct BridgePcmFrame {
    let sequence: Int
    let timestampMs: Int64
    let sampleRate: Int
    let channels: Int
    let bitsPerSample: Int
    let frameSamplesPerChannel: Int
    let pcmData: Data
}

private enum BridgePcmPacketCodec {
    private static let version: UInt8 = 1

    static func encode(_ frame: BridgePcmFrame) -> Data {
        var data = Data()
        data.append(version)
        data.append(UInt8(frame.channels & 0xFF))
        data.appendUInt16LE(UInt16(frame.bitsPerSample & 0xFFFF))
        data.appendUInt32LE(UInt32(bitPattern: Int32(frame.sampleRate)))
        data.appendUInt16LE(UInt16(frame.frameSamplesPerChannel & 0xFFFF))
        data.appendUInt32LE(UInt32(bitPattern: Int32(frame.sequence)))
        data.appendUInt64LE(UInt64(bitPattern: frame.timestampMs))
        data.append(frame.pcmData)
        return data
    }
}

private extension Data {
    mutating func appendUInt16LE(_ value: UInt16) {
        var little = value.littleEndian
        Swift.withUnsafeBytes(of: &little) { bytes in
            append(contentsOf: bytes)
        }
    }

    mutating func appendUInt32LE(_ value: UInt32) {
        var little = value.littleEndian
        Swift.withUnsafeBytes(of: &little) { bytes in
            append(contentsOf: bytes)
        }
    }

    mutating func appendUInt64LE(_ value: UInt64) {
        var little = value.littleEndian
        Swift.withUnsafeBytes(of: &little) { bytes in
            append(contentsOf: bytes)
        }
    }
}
