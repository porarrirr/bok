import Foundation

enum PcmPacketCodec {
    private static let version: UInt8 = 1
    private static let headerSize = 22

    static func encode(_ frame: PcmFrame) -> Data {
        var data = Data()
        data.reserveCapacity(headerSize + frame.pcmData.count)

        data.append(version)
        data.append(UInt8(frame.channels & 0xFF))
        data.appendUInt16LE(UInt16(frame.bitsPerSample & 0xFFFF))
        data.appendUInt32LE(UInt32(bitPattern: Int32(frame.sampleRate)))
        data.appendUInt16LE(UInt16(frame.frameSamplesPerChannel & 0xFFFF))
        data.appendUInt32LE(UInt32(bitPattern: Int32(frame.sequence)))
        data.appendUInt64LE(UInt64(bitPattern: Int64(frame.timestampMs)))
        data.append(frame.pcmData)
        return data
    }

    static func decode(_ packet: Data) -> PcmFrame? {
        guard packet.count >= headerSize else {
            return nil
        }

        let versionValue = packet[0]
        guard versionValue == version else {
            return nil
        }

        let channels = Int(packet[1])
        let bitsPerSample = Int(packet.readUInt16LE(at: 2))
        let sampleRate = Int(Int32(bitPattern: packet.readUInt32LE(at: 4)))
        let frameSamplesPerChannel = Int(packet.readUInt16LE(at: 8))
        let sequence = Int(Int32(bitPattern: packet.readUInt32LE(at: 10)))
        let timestampMs = Int64(bitPattern: packet.readUInt64LE(at: 14))

        guard channels >= 1, channels <= 2 else {
            return nil
        }
        guard bitsPerSample == 16 else {
            return nil
        }
        guard sampleRate > 0, frameSamplesPerChannel > 0 else {
            return nil
        }

        let pcmData = packet.subdata(in: headerSize..<packet.count)
        guard !pcmData.isEmpty else {
            return nil
        }

        return PcmFrame(
            sequence: sequence,
            timestampMs: timestampMs,
            sampleRate: sampleRate,
            channels: channels,
            bitsPerSample: bitsPerSample,
            frameSamplesPerChannel: frameSamplesPerChannel,
            pcmData: pcmData
        )
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

    func readUInt16LE(at offset: Int) -> UInt16 {
        guard offset + 1 < count else {
            return 0
        }
        return UInt16(self[offset])
            | (UInt16(self[offset + 1]) << 8)
    }

    func readUInt32LE(at offset: Int) -> UInt32 {
        guard offset + 3 < count else {
            return 0
        }
        return UInt32(self[offset])
            | (UInt32(self[offset + 1]) << 8)
            | (UInt32(self[offset + 2]) << 16)
            | (UInt32(self[offset + 3]) << 24)
    }

    func readUInt64LE(at offset: Int) -> UInt64 {
        guard offset + 7 < count else {
            return 0
        }
        return UInt64(self[offset])
            | (UInt64(self[offset + 1]) << 8)
            | (UInt64(self[offset + 2]) << 16)
            | (UInt64(self[offset + 3]) << 24)
            | (UInt64(self[offset + 4]) << 32)
            | (UInt64(self[offset + 5]) << 40)
            | (UInt64(self[offset + 6]) << 48)
            | (UInt64(self[offset + 7]) << 56)
    }
}
