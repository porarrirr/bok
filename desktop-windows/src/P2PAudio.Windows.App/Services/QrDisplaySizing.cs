namespace P2PAudio.Windows.App.Services;

public static class QrDisplaySizing
{
    private const int MediumPayloadThreshold = 650;
    private const int DensePayloadThreshold = 900;

    public const double DefaultDisplaySize = 320d;

    public static double GetDisplaySize(int payloadLength) => payloadLength switch
    {
        >= DensePayloadThreshold => 420d,
        >= MediumPayloadThreshold => 380d,
        _ => DefaultDisplaySize
    };
}
