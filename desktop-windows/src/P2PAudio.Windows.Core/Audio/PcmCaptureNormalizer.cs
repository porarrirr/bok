using System.Buffers.Binary;

namespace P2PAudio.Windows.Core.Audio;

public sealed record PcmNormalizationResult(int Channels, byte[] PcmBytes);

public static class PcmCaptureNormalizer
{
    public static int GetOutputChannels(int inputChannels)
    {
        return inputChannels <= 1 ? 1 : 2;
    }

    public static PcmNormalizationResult? NormalizePcm16(ReadOnlySpan<byte> input, int inputChannels)
    {
        if (inputChannels <= 0)
        {
            return null;
        }

        var inputFrameBytes = inputChannels * sizeof(short);
        if (inputFrameBytes <= 0)
        {
            return null;
        }

        var frameCount = input.Length / inputFrameBytes;
        if (frameCount == 0)
        {
            return new PcmNormalizationResult(GetOutputChannels(inputChannels), []);
        }

        var outputChannels = GetOutputChannels(inputChannels);
        var output = new byte[frameCount * outputChannels * sizeof(short)];
        var outputOffset = 0;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frameOffset = frameIndex * inputFrameBytes;
            WriteNormalizedPcm16Frame(input, frameOffset, inputChannels, outputChannels, output, ref outputOffset);
        }

        return new PcmNormalizationResult(outputChannels, output);
    }

    public static PcmNormalizationResult? NormalizeFloat32(ReadOnlySpan<byte> input, int inputChannels)
    {
        if (inputChannels <= 0)
        {
            return null;
        }

        var inputFrameBytes = inputChannels * sizeof(float);
        if (inputFrameBytes <= 0)
        {
            return null;
        }

        var frameCount = input.Length / inputFrameBytes;
        if (frameCount == 0)
        {
            return new PcmNormalizationResult(GetOutputChannels(inputChannels), []);
        }

        var outputChannels = GetOutputChannels(inputChannels);
        var output = new byte[frameCount * outputChannels * sizeof(short)];
        var outputOffset = 0;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frameOffset = frameIndex * inputFrameBytes;
            WriteNormalizedFloatFrame(input, frameOffset, inputChannels, outputChannels, output, ref outputOffset);
        }

        return new PcmNormalizationResult(outputChannels, output);
    }

    private static void WriteNormalizedPcm16Frame(
        ReadOnlySpan<byte> input,
        int frameOffset,
        int inputChannels,
        int outputChannels,
        byte[] output,
        ref int outputOffset)
    {
        if (inputChannels == 1)
        {
            WriteInt16(
                output,
                ref outputOffset,
                BinaryPrimitives.ReadInt16LittleEndian(input.Slice(frameOffset, sizeof(short)))
            );
            return;
        }

        if (inputChannels == 2)
        {
            WriteInt16(
                output,
                ref outputOffset,
                BinaryPrimitives.ReadInt16LittleEndian(input.Slice(frameOffset, sizeof(short)))
            );
            WriteInt16(
                output,
                ref outputOffset,
                BinaryPrimitives.ReadInt16LittleEndian(input.Slice(frameOffset + sizeof(short), sizeof(short)))
            );
            return;
        }

        var mixed = MixPcm16ChannelsToStereo(input, frameOffset, inputChannels);
        WriteInt16(output, ref outputOffset, mixed);
        if (outputChannels == 2)
        {
            WriteInt16(output, ref outputOffset, mixed);
        }
    }

    private static void WriteNormalizedFloatFrame(
        ReadOnlySpan<byte> input,
        int frameOffset,
        int inputChannels,
        int outputChannels,
        byte[] output,
        ref int outputOffset)
    {
        if (inputChannels == 1)
        {
            WriteInt16(output, ref outputOffset, ReadFloatSample(input, frameOffset));
            return;
        }

        if (inputChannels == 2)
        {
            WriteInt16(output, ref outputOffset, ReadFloatSample(input, frameOffset));
            WriteInt16(output, ref outputOffset, ReadFloatSample(input, frameOffset + sizeof(float)));
            return;
        }

        var mixed = MixFloatChannelsToStereo(input, frameOffset, inputChannels);
        WriteInt16(output, ref outputOffset, mixed);
        if (outputChannels == 2)
        {
            WriteInt16(output, ref outputOffset, mixed);
        }
    }

    private static short MixPcm16ChannelsToStereo(ReadOnlySpan<byte> input, int frameOffset, int inputChannels)
    {
        var sum = 0;
        for (var channelIndex = 0; channelIndex < inputChannels; channelIndex++)
        {
            sum += BinaryPrimitives.ReadInt16LittleEndian(
                input.Slice(frameOffset + (channelIndex * sizeof(short)), sizeof(short))
            );
        }

        return (short)(sum / inputChannels);
    }

    private static short MixFloatChannelsToStereo(ReadOnlySpan<byte> input, int frameOffset, int inputChannels)
    {
        var sum = 0;
        for (var channelIndex = 0; channelIndex < inputChannels; channelIndex++)
        {
            sum += ReadFloatSample(input, frameOffset + (channelIndex * sizeof(float)));
        }

        return (short)(sum / inputChannels);
    }

    private static short ReadFloatSample(ReadOnlySpan<byte> input, int offset)
    {
        var floatBits = BinaryPrimitives.ReadInt32LittleEndian(input.Slice(offset, sizeof(float)));
        return FloatToInt16(BitConverter.Int32BitsToSingle(floatBits));
    }

    private static short FloatToInt16(float sample)
    {
        var clamped = Math.Clamp(sample, -1.0f, 1.0f);
        return (short)(clamped * short.MaxValue);
    }

    private static void WriteInt16(byte[] output, ref int outputOffset, short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(outputOffset, sizeof(short)), value);
        outputOffset += sizeof(short);
    }
}
