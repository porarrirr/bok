import AVFoundation
import Foundation

final class AudioPlaybackSession {
    static let shared = AudioPlaybackSession()

    private let lock = NSLock()

    private init() {}

    func activateForPlayback() throws {
        lock.lock()
        defer { lock.unlock() }

        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.playback, mode: .default, options: [.allowAirPlay])
        try session.setPreferredSampleRate(48_000)
        try session.setPreferredIOBufferDuration(0.02)
        try session.setActive(true)
    }
}
