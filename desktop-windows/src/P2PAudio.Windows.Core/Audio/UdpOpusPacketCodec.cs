using System.Buffers.Binary;

namespace P2PAudio.Windows.Core.Audio;

public static class UdpOpusPacketCodec
{
    private static readonly byte[] Magic = [(byte)'P', (byte)'2', (byte)'A', (byte)'U'];
    private const byte Version = 1;

    public const int HeaderBytes = 26;

    public static byte[] Encode(UdpOpusPacket packet)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(packet.SampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(packet.Channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(packet.FrameSamplesPerChannel);
        ArgumentNullException.ThrowIfNull(packet.OpusPayload);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(packet.OpusPayload.Length);
        if (packet.Channels is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(packet), "Channels must be 1 or 2.");
        }

        var buffer = new byte[HeaderBytes + packet.OpusPayload.Length];
        Magic.CopyTo(buffer, 0);
        buffer[4] = Version;
        buffer[5] = checked((byte)packet.Channels);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(6, 2), checked((ushort)packet.FrameSamplesPerChannel));
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(8, 4), packet.SampleRate);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(12, 4), packet.Sequence);
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(16, 8), packet.TimestampMs);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(24, 2), checked((ushort)packet.OpusPayload.Length));
        packet.OpusPayload.CopyTo(buffer, HeaderBytes);
        return buffer;
    }

    public static UdpOpusPacket? Decode(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length < HeaderBytes)
        {
            return null;
        }

        if (!raw.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            return null;
        }

        if (raw[4] != Version)
        {
            return null;
        }

        var channels = raw[5];
        var frameSamplesPerChannel = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(6, 2));
        var sampleRate = BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(8, 4));
        var sequence = BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(12, 4));
        var timestampMs = BinaryPrimitives.ReadInt64BigEndian(raw.AsSpan(16, 8));
        var payloadSize = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(24, 2));
        if (channels is < 1 or > 2 || frameSamplesPerChannel == 0 || sampleRate <= 0 || payloadSize == 0)
        {
            return null;
        }

        if (raw.Length != HeaderBytes + payloadSize)
        {
            return null;
        }

        return new UdpOpusPacket(
            Sequence: sequence,
            TimestampMs: timestampMs,
            SampleRate: sampleRate,
            Channels: channels,
            FrameSamplesPerChannel: frameSamplesPerChannel,
            OpusPayload: raw.AsSpan(HeaderBytes, payloadSize).ToArray()
        );
    }
}
