using System.Buffers.Binary;

namespace P2PAudio.Windows.Core.Audio;

public static class PcmPacketCodec
{
    private const byte Version = 1;
    private const int HeaderSize = 22;

    public static byte[] Encode(PcmFrame frame)
    {
        var packet = new byte[HeaderSize + frame.PcmBytes.Length];
        packet[0] = Version;
        packet[1] = (byte)frame.Channels;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)frame.BitsPerSample);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), (uint)frame.SampleRate);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(8, 2), (ushort)frame.FrameSamplesPerChannel);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(10, 4), unchecked((uint)frame.Sequence));
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(14, 8), unchecked((ulong)frame.TimestampMs));
        frame.PcmBytes.CopyTo(packet, HeaderSize);
        return packet;
    }

    public static PcmFrame? Decode(byte[] packet)
    {
        if (packet.Length < HeaderSize)
        {
            return null;
        }
        if (packet[0] != Version)
        {
            return null;
        }

        var channels = packet[1];
        var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));
        var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4));
        var frameSamples = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(8, 2));
        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(10, 4));
        var timestampMs = BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(14, 8));

        if (channels is < 1 or > 2)
        {
            return null;
        }
        if (bitsPerSample != 16 || sampleRate == 0 || frameSamples == 0)
        {
            return null;
        }

        var pcmLength = packet.Length - HeaderSize;
        if (pcmLength <= 0)
        {
            return null;
        }
        var pcmBytes = new byte[pcmLength];
        Buffer.BlockCopy(packet, HeaderSize, pcmBytes, 0, pcmLength);

        return new PcmFrame(
            Sequence: unchecked((int)sequence),
            TimestampMs: unchecked((long)timestampMs),
            SampleRate: unchecked((int)sampleRate),
            Channels: channels,
            BitsPerSample: bitsPerSample,
            FrameSamplesPerChannel: frameSamples,
            PcmBytes: pcmBytes
        );
    }
}
