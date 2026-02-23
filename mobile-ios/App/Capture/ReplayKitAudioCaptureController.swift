import Foundation
import ReplayKit

final class ReplayKitAudioCaptureController: ObservableObject {
    @Published var isBroadcastActive = false

    private let sharedDefaults = UserDefaults(suiteName: ReplayKitAudioCaptureController.appGroupId)
    private let ioQueue = DispatchQueue(label: "com.example.p2paudio.replaykit-consumer")

    private var pollTimer: DispatchSourceTimer?
    private var readOffset = 0
    private var frameHandler: ((PcmFrame) -> Void)?

    func refreshBroadcastState() {
        isBroadcastActive = sharedDefaults?.bool(forKey: Self.broadcastActiveKey) ?? false
    }

    func startConsumingFrames(_ onFrame: @escaping (PcmFrame) -> Void) {
        frameHandler = onFrame
        ioQueue.async { [weak self] in
            guard let self else { return }
            self.startTimerIfNeeded()
        }
    }

    func stopConsumingFrames() {
        ioQueue.async { [weak self] in
            guard let self else { return }
            self.pollTimer?.cancel()
            self.pollTimer = nil
            self.readOffset = 0
            self.frameHandler = nil
        }
    }

    func stopBroadcastFlag() {
        sharedDefaults?.set(false, forKey: Self.broadcastActiveKey)
        isBroadcastActive = false
        stopConsumingFrames()
    }

    private func startTimerIfNeeded() {
        if pollTimer != nil {
            return
        }
        let timer = DispatchSource.makeTimerSource(queue: ioQueue)
        timer.schedule(deadline: .now() + .milliseconds(20), repeating: .milliseconds(20))
        timer.setEventHandler { [weak self] in
            self?.pollBridgeFile()
        }
        pollTimer = timer
        timer.resume()
    }

    private func pollBridgeFile() {
        guard let fileURL = bridgeFileURL else {
            return
        }
        guard let data = try? Data(contentsOf: fileURL), !data.isEmpty else {
            return
        }

        var cursor = readOffset
        if cursor > data.count {
            cursor = 0
        }

        while cursor + 4 <= data.count {
            let packetLength = Int(data.readUInt32LE(at: cursor))
            if packetLength <= 0 {
                break
            }
            let packetStart = cursor + 4
            let packetEnd = packetStart + packetLength
            if packetEnd > data.count {
                break
            }

            let packet = data.subdata(in: packetStart..<packetEnd)
            if let frame = PcmPacketCodec.decode(packet) {
                frameHandler?(frame)
            }
            cursor = packetEnd
        }

        readOffset = cursor
    }

    private var bridgeFileURL: URL? {
        guard let container = FileManager.default.containerURL(
            forSecurityApplicationGroupIdentifier: Self.appGroupId
        ) else {
            return nil
        }
        return container.appendingPathComponent(Self.bridgeFileName)
    }

    private static let appGroupId = "group.com.example.p2paudio"
    private static let broadcastActiveKey = "broadcast_active"
    private static let bridgeFileName = "replaykit_pcm_bridge.bin"
}

private extension Data {
    func readUInt32LE(at offset: Int) -> UInt32 {
        guard offset + 3 < count else {
            return 0
        }
        return UInt32(self[offset])
            | (UInt32(self[offset + 1]) << 8)
            | (UInt32(self[offset + 2]) << 16)
            | (UInt32(self[offset + 3]) << 24)
    }
}
