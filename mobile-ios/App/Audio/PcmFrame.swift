import Foundation

struct PcmFrame {
    let sequence: Int
    let timestampMs: Int64
    let sampleRate: Int
    let channels: Int
    let bitsPerSample: Int
    let frameSamplesPerChannel: Int
    let pcmData: Data
}
