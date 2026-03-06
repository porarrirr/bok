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
    case usbTetherUnavailable = "usb_tether_unavailable"
    case usbTetherDetectedButNotReachable = "usb_tether_detected_but_not_reachable"
    case networkInterfaceNotUsable = "network_interface_not_usable"
    case sessionExpired = "session_expired"
    case invalidPayload = "invalid_payload"
}

struct SessionFailure: Error {
    let code: FailureCode
    let message: String
}

enum NetworkPathType: String {
    case wifiLan = "wifi_lan"
    case usbTether = "usb_tether"
    case unknown = "unknown"
}

struct ConnectionDiagnostics {
    let pathType: NetworkPathType
    let localCandidatesCount: Int
    let selectedCandidatePairType: String
    let failureHint: String

    init(
        pathType: NetworkPathType = .unknown,
        localCandidatesCount: Int = 0,
        selectedCandidatePairType: String = "",
        failureHint: String = ""
    ) {
        self.pathType = pathType
        self.localCandidatesCount = localCandidatesCount
        self.selectedCandidatePairType = selectedCandidatePairType
        self.failureHint = failureHint
    }
}
