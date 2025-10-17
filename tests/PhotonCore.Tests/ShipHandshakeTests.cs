// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using PSO.Auth;
using PSO.Net;
using PSO.Proto;
using PSO.Proto.Compat;
using PSO.Ship;
using Xunit;

namespace PhotonCore.Tests;

public class ShipHandshakeTests
{
    [Fact]
    public void JoinRequest_RoundTrips()
    {
        var request = new ShipJoinRequest("ticket-data");
        var encoded = PcV2ShipCodec.WriteJoinRequest(request);
        Assert.True(PcV2ShipCodec.TryReadJoinRequest(encoded, out var decoded));
        Assert.Equal(request.Ticket, decoded.Ticket);
    }

    [Fact]
    public void JoinAck_RoundTrips()
    {
        var ack = new ShipJoinAck(0, "welcome");
        var encoded = PcV2ShipCodec.WriteJoinAck(ack);
        Assert.True(PcV2ShipCodec.TryReadJoinAck(encoded, out var decoded));
        Assert.Equal(ack.ResultCode, decoded.ResultCode);
        Assert.Equal(ack.Message, decoded.Message);
    }

    [Fact]
    public void PingPong_RoundTrips()
    {
        var ping = new ShipPing(42);
        var encodedPing = PcV2ShipCodec.WritePing(ping);
        Assert.True(PcV2ShipCodec.TryReadPing(encodedPing, out var decodedPing));
        Assert.Equal(ping.Seq, decodedPing.Seq);

        var pong = new ShipPong(99);
        var encodedPong = PcV2ShipCodec.WritePong(pong);
        Assert.True(PcV2ShipCodec.TryReadPong(encodedPong, out var decodedPong));
        Assert.Equal(pong.Seq, decodedPong.Seq);
    }

    [Fact]
    public async Task ShipSession_CompletesJoinAndHeartbeatFlow()
    {
        var tickets = CreateTickets();
        var accountId = Guid.NewGuid();
        var token = tickets.Issue(accountId).Token;
        var logs = new ConcurrentQueue<string>();

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = AcceptAndRunSessionAsync(listener, tickets, logs, TimeSpan.FromMilliseconds(250));

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var stream = client.GetStream();

        await TcpHelpers.WriteFrameAsync(stream, PcV2ShipCodec.WriteJoinRequest(new ShipJoinRequest(token)), format: FrameFormat.PcV2);
        var joinAckPayload = await TcpHelpers.ReadFrameAsync(stream, format: FrameFormat.PcV2);
        Assert.NotNull(joinAckPayload);
        Assert.True(PcV2ShipCodec.TryReadJoinAck(joinAckPayload!, out var joinAck));
        Assert.Equal((byte)0, joinAck.ResultCode);
        Assert.Equal(accountId.ToString(), joinAck.Message);

        await TcpHelpers.WriteFrameAsync(stream, PcV2ShipCodec.WritePing(new ShipPing(1)), format: FrameFormat.PcV2);
        var pongPayload = await TcpHelpers.ReadFrameAsync(stream, format: FrameFormat.PcV2);
        Assert.NotNull(pongPayload);
        Assert.True(PcV2ShipCodec.TryReadPong(pongPayload!, out var pong));
        Assert.Equal((uint)1, pong.Seq);

        await Task.Delay(TimeSpan.FromMilliseconds(400));
        var buffer = new byte[1];
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = await stream.ReadAsync(buffer.AsMemory(), timeoutCts.Token);
        Assert.Equal(0, read);

        await AssertCompletedAsync(serverTask);
        Assert.Contains(logs, entry => entry.Contains("event=heartbeat type=ping"));
        Assert.Contains(logs, entry => entry.Contains("event=close reason=timeout"));
    }

    [Fact]
    public async Task ShipSession_RejectsInvalidTicket()
    {
        var tickets = CreateTickets();
        var logs = new ConcurrentQueue<string>();

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = AcceptAndRunSessionAsync(listener, tickets, logs, TimeSpan.FromMilliseconds(250));

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var stream = client.GetStream();

        await TcpHelpers.WriteFrameAsync(stream, PcV2ShipCodec.WriteJoinRequest(new ShipJoinRequest("invalid")), format: FrameFormat.PcV2);
        var joinAckPayload = await TcpHelpers.ReadFrameAsync(stream, format: FrameFormat.PcV2);
        Assert.NotNull(joinAckPayload);
        Assert.True(PcV2ShipCodec.TryReadJoinAck(joinAckPayload!, out var joinAck));
        Assert.NotEqual((byte)0, joinAck.ResultCode);
        Assert.Equal("invalid ticket", joinAck.Message);

        var buffer = new byte[1];
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = await stream.ReadAsync(buffer.AsMemory(), timeoutCts.Token);
        Assert.Equal(0, read);

        await AssertCompletedAsync(serverTask);
        Assert.Contains(logs, entry => entry.Contains("status=invalid_ticket"));
    }

    private static Tickets CreateTickets()
    {
        var secret = Enumerable.Repeat((byte)0x42, 32).ToArray();
        return new Tickets(secret, defaultTtl: TimeSpan.FromSeconds(60));
    }

    private static async Task AcceptAndRunSessionAsync(
        TcpListener listener,
        Tickets tickets,
        ConcurrentQueue<string> logs,
        TimeSpan heartbeatTimeout)
    {
        var client = await listener.AcceptTcpClientAsync();
        await ShipSession.RunAsync(client, tickets, logs.Enqueue, heartbeatTimeout);
    }

    private static async Task AssertCompletedAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(task, completed);
        await task;
    }
}
