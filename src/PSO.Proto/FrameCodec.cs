using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PSO.Proto;
public enum FrameFormat
{
    PhotonCore,
    PcV2,
}

public static class FrameCodec
{
    public static byte[] Encode(ReadOnlySpan<byte> payload, FrameFormat format = FrameFormat.PhotonCore)
    {
        return format switch
        {
            FrameFormat.PhotonCore => EncodePhotonCore(payload),
            FrameFormat.PcV2 => EncodePcV2(payload),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    public static (bool complete, byte[]? payload) TryDecode(ref List<byte> buffer, FrameFormat format = FrameFormat.PhotonCore)
    {
        return format switch
        {
            FrameFormat.PhotonCore => TryDecodePhotonCore(ref buffer),
            FrameFormat.PcV2 => TryDecodePcV2(ref buffer),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    private static byte[] EncodePhotonCore(ReadOnlySpan<byte> payload)
    {
        var buf = new byte[2 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0, 2), (ushort)payload.Length);
        payload.CopyTo(buf.AsSpan(2));
        return buf;
    }

    private static byte[] EncodePcV2(ReadOnlySpan<byte> payload)
    {
        // PC V2 commands carry their own size header; we expect callers to have produced
        // a complete command frame already.
        return payload.ToArray();
    }

    private static (bool, byte[]?) TryDecodePhotonCore(ref List<byte> buffer)
    {
        if (buffer.Count < 2)
        {
            return (false, null);
        }

        var span = CollectionsMarshal.AsSpan(buffer);
        var len = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(0, 2));
        if (buffer.Count < 2 + len)
        {
            return (false, null);
        }

        var payload = buffer.GetRange(2, len).ToArray();
        buffer.RemoveRange(0, 2 + len);
        return (true, payload);
    }

    private static (bool, byte[]?) TryDecodePcV2(ref List<byte> buffer)
    {
        if (buffer.Count < 2)
        {
            return (false, null);
        }

        var span = CollectionsMarshal.AsSpan(buffer);
        var len = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0, 2));
        if (len < 4)
        {
            throw new InvalidOperationException("PC V2 frame length smaller than header");
        }

        if (buffer.Count < len)
        {
            return (false, null);
        }

        var payload = buffer.GetRange(0, len).ToArray();
        buffer.RemoveRange(0, len);
        return (true, payload);
    }
}
