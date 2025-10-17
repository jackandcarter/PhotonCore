using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using PSO.Net;
using PSO.Proto;
using Xunit;

namespace PhotonCore.Tests;

public class FrameClampTests
{
    [Fact]
    public async Task OversizedFrameClosesConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);

        var acceptTask = listener.AcceptTcpClientAsync();

        var payload = new byte[TcpHelpers.MaxFrameSize + 10];
        var frame = FrameCodec.Encode(payload, FrameFormat.PhotonCore);

        var serverTask = Task.Run(async () =>
        {
            using var serverClient = await acceptTask;
            using var networkStream = serverClient.GetStream();
            await Assert.ThrowsAsync<TcpHelpers.FrameTooLargeException>(async () =>
                await TcpHelpers.ReadFrameAsync(networkStream));
            Assert.False(serverClient.Connected);
        });

        await client.GetStream().WriteAsync(frame);
        await client.GetStream().FlushAsync();

        await serverTask;
    }
}
