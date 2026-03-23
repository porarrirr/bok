namespace P2PAudio.Windows.App.Services;

public enum UdpReceiveLatencyPreset
{
    Ms20,
    Ms50,
    Ms100,
    Ms300
}

public static class UdpReceiveLatencyPresetExtensions
{
    public static PcmPlaybackProfile ToPlaybackProfile(this UdpReceiveLatencyPreset preset)
    {
        return preset switch
        {
            UdpReceiveLatencyPreset.Ms20 => new PcmPlaybackProfile(DesiredLatencyMs: 40, BufferDurationMs: 180),
            UdpReceiveLatencyPreset.Ms50 => new PcmPlaybackProfile(DesiredLatencyMs: 80, BufferDurationMs: 260),
            UdpReceiveLatencyPreset.Ms100 => new PcmPlaybackProfile(DesiredLatencyMs: 120, BufferDurationMs: 360),
            UdpReceiveLatencyPreset.Ms300 => new PcmPlaybackProfile(DesiredLatencyMs: 240, BufferDurationMs: 700),
            _ => PcmPlaybackService.DefaultProfile
        };
    }
}
