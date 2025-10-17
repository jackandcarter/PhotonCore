using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PSO.Proto;
public static class FrameCodec
{
    public static byte[] Encode(ReadOnlySpan<byte> payload) {
        var buf = new byte[2 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(0,2), (ushort)payload.Length);
        payload.CopyTo(buf.AsSpan(2));
        return buf;
    }
    public static (bool complete, byte[]? payload) TryDecode(ref List<byte> buffer) {
        if (buffer.Count < 2) return (false, null);
        ushort len = BinaryPrimitives.ReadUInt16BigEndian(CollectionsMarshal.AsSpan(buffer).Slice(0,2));
        if (buffer.Count < 2 + len) return (false, null);
        var payload = buffer.GetRange(2, len).ToArray();
        buffer.RemoveRange(0, 2 + len);
        return (true, payload);
    }
}
