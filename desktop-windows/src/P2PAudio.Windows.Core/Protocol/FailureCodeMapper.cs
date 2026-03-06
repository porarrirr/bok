using P2PAudio.Windows.Core.Models;

namespace P2PAudio.Windows.Core.Protocol;

public static class FailureCodeMapper
{
    public static FailureCode? FromFailureHint(string? rawHint)
    {
        if (string.IsNullOrWhiteSpace(rawHint))
        {
            return null;
        }

        var hint = rawHint.Trim().ToLowerInvariant();

        if (hint.Contains("permission_denied", StringComparison.Ordinal))
        {
            return FailureCode.PermissionDenied;
        }
        if (hint.Contains("audio_capture_not_supported", StringComparison.Ordinal))
        {
            return FailureCode.AudioCaptureNotSupported;
        }
        if (hint.Contains("usb_tether_unavailable", StringComparison.Ordinal))
        {
            return FailureCode.UsbTetherUnavailable;
        }
        if (hint.Contains("usb_tether_detected_but_not_reachable", StringComparison.Ordinal))
        {
            return FailureCode.UsbTetherDetectedButNotReachable;
        }
        if (hint.Contains("network_interface_not_usable", StringComparison.Ordinal))
        {
            return FailureCode.NetworkInterfaceNotUsable;
        }
        if (hint.Contains("session_expired", StringComparison.Ordinal))
        {
            return FailureCode.SessionExpired;
        }
        if (hint.Contains("invalid_payload", StringComparison.Ordinal))
        {
            return FailureCode.InvalidPayload;
        }
        if (hint.Contains("network_changed", StringComparison.Ordinal) ||
            hint.Contains("ice_disconnected", StringComparison.Ordinal))
        {
            return FailureCode.NetworkChanged;
        }
        if (hint.Contains("data_channel_open_timeout", StringComparison.Ordinal) ||
            hint.Contains("data_channel_not_open", StringComparison.Ordinal) ||
            hint.Contains("data_channel_not_available", StringComparison.Ordinal) ||
            hint.Contains("send_pcm_failed", StringComparison.Ordinal) ||
            hint.Contains("peer_unreachable", StringComparison.Ordinal))
        {
            return FailureCode.PeerUnreachable;
        }
        if (hint.Contains("webrtc_negotiation_failed", StringComparison.Ordinal) ||
            hint.Contains("create_peer_connection_failed", StringComparison.Ordinal) ||
            hint.Contains("set_remote_offer_failed", StringComparison.Ordinal) ||
            hint.Contains("set_remote_answer_failed", StringComparison.Ordinal) ||
            hint.Contains("set_local_description_offer_failed", StringComparison.Ordinal) ||
            hint.Contains("set_local_answer_failed", StringComparison.Ordinal) ||
            hint.Contains("local_offer_timeout", StringComparison.Ordinal) ||
            hint.Contains("local_answer_timeout", StringComparison.Ordinal) ||
            hint.Contains("ice_gathering_timeout", StringComparison.Ordinal) ||
            hint.Contains("ice_failed", StringComparison.Ordinal) ||
            hint.Contains("native_backend_unavailable", StringComparison.Ordinal) ||
            hint.Contains("native_stub_backend", StringComparison.Ordinal) ||
            hint.Contains("libdatachannel_disabled", StringComparison.Ordinal))
        {
            return FailureCode.WebRtcNegotiationFailed;
        }

        return null;
    }

    public static string ToWireValue(FailureCode code)
    {
        return code switch
        {
            FailureCode.PermissionDenied => "permission_denied",
            FailureCode.AudioCaptureNotSupported => "audio_capture_not_supported",
            FailureCode.WebRtcNegotiationFailed => "webrtc_negotiation_failed",
            FailureCode.PeerUnreachable => "peer_unreachable",
            FailureCode.NetworkChanged => "network_changed",
            FailureCode.UsbTetherUnavailable => "usb_tether_unavailable",
            FailureCode.UsbTetherDetectedButNotReachable => "usb_tether_detected_but_not_reachable",
            FailureCode.NetworkInterfaceNotUsable => "network_interface_not_usable",
            FailureCode.SessionExpired => "session_expired",
            FailureCode.InvalidPayload => "invalid_payload",
            _ => code.ToString().ToLowerInvariant()
        };
    }
}
