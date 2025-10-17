// SPDX-License-Identifier: Apache-2.0
using System.Net.Sockets;
using PSO.Auth;
using PSO.Net;
using PSO.Proto;
using PSO.Proto.Compat;

namespace PSO.Ship;

public static class ShipSession
{
    private static readonly TimeSpan DefaultHeartbeatTimeout = TimeSpan.FromSeconds(20);

    public static async Task RunAsync(
        TcpClient client,
        Tickets tickets,
        Action<string> log,
        TimeSpan? heartbeatTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(tickets);
        log ??= static _ => { };

        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var closeReason = "disconnect";
        string? closeError = null;
        var heartbeatWindow = heartbeatTimeout ?? DefaultHeartbeatTimeout;
        var lastPingAt = DateTimeOffset.UtcNow;

        log($"[ship] event=connect remote={remote}");

        try
        {
            using var stream = client.GetStream();

            ShipJoinRequest joinRequest;
            byte[]? frame;
            try
            {
                frame = await TcpHelpers.ReadFrameAsync(stream, cancellationToken, FrameFormat.PcV2);
            }
            catch (TcpHelpers.FrameTooLargeException)
            {
                closeReason = "frame_too_large";
                log($"[ship] event=join status=error reason=frame_too_large remote={remote}");
                await WriteJoinAckSafeAsync(stream, new ShipJoinAck(3, "frame too large"), cancellationToken);
                return;
            }

            if (frame is null)
            {
                closeReason = "no_join";
                return;
            }

            if (!PcV2ShipCodec.TryReadJoinRequest(frame, out joinRequest))
            {
                closeReason = "invalid_join";
                log($"[ship] event=join status=invalid_join remote={remote}");
                await WriteJoinAckSafeAsync(stream, new ShipJoinAck(1, "invalid join"), cancellationToken);
                return;
            }

            if (!tickets.TryValidate(joinRequest.Ticket, out var accountId))
            {
                closeReason = "invalid_ticket";
                log($"[ship] event=join status=invalid_ticket remote={remote}");
                await WriteJoinAckSafeAsync(stream, new ShipJoinAck(2, "invalid ticket"), cancellationToken);
                return;
            }

            var ack = new ShipJoinAck(0, accountId.ToString());
            await TcpHelpers.WriteFrameAsync(stream, PcV2ShipCodec.WriteJoinAck(ack), cancellationToken, FrameFormat.PcV2);
            log($"[ship] event=join status=ok account={accountId} remote={remote}");
            lastPingAt = DateTimeOffset.UtcNow;

            while (true)
            {
                var readTask = TcpHelpers.ReadFrameAsync(stream, cancellationToken, FrameFormat.PcV2);
                var delayTask = Task.Delay(heartbeatWindow, cancellationToken);
                var completed = await Task.WhenAny(readTask, delayTask);

                if (completed == delayTask)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        closeReason = "cancelled";
                        break;
                    }

                    if (DateTimeOffset.UtcNow - lastPingAt >= heartbeatWindow)
                    {
                        closeReason = "timeout";
                        break;
                    }

                    continue;
                }

                byte[]? payload;
                try
                {
                    payload = await readTask;
                }
                catch (TcpHelpers.FrameTooLargeException)
                {
                    closeReason = "frame_too_large";
                    break;
                }

                if (payload is null)
                {
                    closeReason = "disconnect";
                    break;
                }

                if (PcV2ShipCodec.TryReadPing(payload, out var ping))
                {
                    lastPingAt = DateTimeOffset.UtcNow;
                    log($"[ship] event=heartbeat type=ping seq={ping.Seq} remote={remote}");
                    var pong = new ShipPong(ping.Seq);
                    await TcpHelpers.WriteFrameAsync(stream, PcV2ShipCodec.WritePong(pong), cancellationToken, FrameFormat.PcV2);
                    log($"[ship] event=heartbeat type=pong seq={pong.Seq} remote={remote}");
                }
            }
        }
        catch (Exception ex)
        {
            closeReason = closeReason == "disconnect" ? "error" : closeReason;
            closeError = ex.Message;
        }
        finally
        {
            try
            {
                client.Close();
            }
            catch
            {
                // ignored
            }

            var message = $"[ship] event=close reason={closeReason} remote={remote}";
            if (!string.IsNullOrEmpty(closeError))
            {
                message += $" error=\"{closeError}\"";
            }

            log(message);
        }
    }

    private static async Task WriteJoinAckSafeAsync(NetworkStream stream, ShipJoinAck ack, CancellationToken cancellationToken)
    {
        try
        {
            await TcpHelpers.WriteFrameAsync(stream, PcV2ShipCodec.WriteJoinAck(ack), cancellationToken, FrameFormat.PcV2);
        }
        catch
        {
            // best effort acknowledgement
        }
    }
}
