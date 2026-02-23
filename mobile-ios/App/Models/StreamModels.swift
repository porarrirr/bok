import Foundation

enum AudioStreamState: String {
    case idle
    case capturing
    case connecting
    case streaming
    case interrupted
    case failed
    case ended
}

enum FailureCode: String {
    case permissionDenied = "permission_denied"
    case audioCaptureNotSupported = "audio_capture_not_supported"
    case webrtcNegotiationFailed = "webrtc_negotiation_failed"
    case peerUnreachable = "peer_unreachable"
    case networkChanged = "network_changed"
    case sessionExpired = "session_expired"
    case invalidPayload = "invalid_payload"
}

struct SessionFailure: Error {
    let code: FailureCode
    let message: String
}
