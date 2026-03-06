namespace P2PAudio.Windows.Core.Models;

public enum FailureCode
{
    PermissionDenied,
    AudioCaptureNotSupported,
    WebRtcNegotiationFailed,
    PeerUnreachable,
    NetworkChanged,
    UsbTetherUnavailable,
    UsbTetherDetectedButNotReachable,
    NetworkInterfaceNotUsable,
    SessionExpired,
    InvalidPayload
}
