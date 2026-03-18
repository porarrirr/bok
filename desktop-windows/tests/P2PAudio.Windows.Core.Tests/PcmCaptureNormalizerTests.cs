using System.Buffers.Binary;
using P2PAudio.Windows.Core.Audio;

namespace P2PAudio.Windows.Core.Tests;

public sealed class PcmCaptureNormalizerTests
{
    [Fact]
    public void NormalizePcm16_PreservesStereoPayload()
    {
        var input = new byte[8];
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(0, 2), 1000);
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(2, 2), -1000);
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(4, 2), 2000);
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(6, 2), -2000);

        var normalized = PcmCaptureNormalizer.NormalizePcm16(input, inputChannels: 2);

        Assert.NotNull(normalized);
        Assert.Equal(2, normalized!.Channels);
        Assert.Equal(input, normalized.PcmBytes);
    }

    [Fact]
    public void NormalizePcm16_DownmixesMultiChannelInputToStereo()
    {
        var input = new byte[8];
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(0, 2), 1000);
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(2, 2), 3000);
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(4, 2), 5000);
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(6, 2), 7000);

        var normalized = PcmCaptureNormalizer.NormalizePcm16(input, inputChannels: 4);

        Assert.NotNull(normalized);
        Assert.Equal(2, normalized!.Channels);
        Assert.Equal(4, normalized.PcmBytes.Length);
        Assert.Equal(4000, BinaryPrimitives.ReadInt16LittleEndian(normalized.PcmBytes.AsSpan(0, 2)));
        Assert.Equal(4000, BinaryPrimitives.ReadInt16LittleEndian(normalized.PcmBytes.AsSpan(2, 2)));
    }

    [Fact]
    public void NormalizeFloat32_ConvertsToPcm16()
    {
        var input = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(0, 4), BitConverter.SingleToInt32Bits(0.5f));
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(4, 4), BitConverter.SingleToInt32Bits(-0.5f));

        var normalized = PcmCaptureNormalizer.NormalizeFloat32(input, inputChannels: 2);

        Assert.NotNull(normalized);
        Assert.Equal(2, normalized!.Channels);
        Assert.Equal(16383, BinaryPrimitives.ReadInt16LittleEndian(normalized.PcmBytes.AsSpan(0, 2)));
        Assert.Equal(-16383, BinaryPrimitives.ReadInt16LittleEndian(normalized.PcmBytes.AsSpan(2, 2)));
    }
}
