using System.Net.Sockets;
using PSO.Proto;

namespace PSO.Net;
public static class TcpHelpers
{
    public static async Task WriteFrameAsync(NetworkStream ns, ReadOnlyMemory<byte> payload, CancellationToken ct = default, FrameFormat format = FrameFormat.PhotonCore)
        => await ns.WriteAsync(FrameCodec.Encode(payload.Span, format), ct);

    public static async Task<byte[]?> ReadFrameAsync(NetworkStream ns, CancellationToken ct = default, FrameFormat format = FrameFormat.PhotonCore)
    {
        var buf = new List<byte>(8192);
        var tmp = new byte[4096];
        while (true) {
            var (ok, payload) = FrameCodec.TryDecode(ref buf, format);
            if (ok) return payload;
            int n = await ns.ReadAsync(tmp.AsMemory(), ct);
            if (n == 0) return null;
            buf.AddRange(tmp.AsSpan(0, n).ToArray());
        }
    }
    public static string Banner(string name) => $"PHOTONCORE {name} READY";
}
