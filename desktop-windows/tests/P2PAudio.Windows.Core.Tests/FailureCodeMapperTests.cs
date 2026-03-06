using P2PAudio.Windows.Core.Models;
using P2PAudio.Windows.Core.Protocol;

namespace P2PAudio.Windows.Core.Tests;

public sealed class FailureCodeMapperTests
{
    [Theory]
    [InlineData("permission_denied", FailureCode.PermissionDenied)]
    [InlineData("audio_capture_not_supported", FailureCode.AudioCaptureNotSupported)]
    [InlineData("usb_tether_unavailable", FailureCode.UsbTetherUnavailable)]
    [InlineData("usb_tether_detected_but_not_reachable", FailureCode.UsbTetherDetectedButNotReachable)]
    [InlineData("network_interface_not_usable", FailureCode.NetworkInterfaceNotUsable)]
    [InlineData("ice_disconnected", FailureCode.NetworkChanged)]
    [InlineData("data_channel_open_timeout", FailureCode.PeerUnreachable)]
    [InlineData("send_pcm_failed_-1", FailureCode.PeerUnreachable)]
    [InlineData("set_remote_answer_failed_-1", FailureCode.WebRtcNegotiationFailed)]
    [InlineData("session_expired", FailureCode.SessionExpired)]
    [InlineData("invalid_payload", FailureCode.InvalidPayload)]
    public void FromFailureHint_MapsExpectedCode(string hint, FailureCode expected)
    {
        var actual = FailureCodeMapper.FromFailureHint(hint);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ToWireValue_UsesProtocolName()
    {
        var wire = FailureCodeMapper.ToWireValue(FailureCode.WebRtcNegotiationFailed);

        Assert.Equal("webrtc_negotiation_failed", wire);
    }

    [Fact]
    public void FromFailureHint_UnknownHint_ReturnsNull()
    {
        var actual = FailureCodeMapper.FromFailureHint("unclassified_error");

        Assert.Null(actual);
    }
}
