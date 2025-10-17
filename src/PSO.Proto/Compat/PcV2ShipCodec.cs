// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Text;

namespace PSO.Proto.Compat;

public static class PcV2ShipCodec
{
    private const byte JoinRequestCommand = 0x61;
    private const byte JoinAckCommand = 0x62;
    private const byte PingCommand = 0x63;
    private const byte PongCommand = 0x64;

    public static bool TryReadJoinRequest(ReadOnlySpan<byte> frame, out ShipJoinRequest request)
    {
        if (!TryGetBody(frame, JoinRequestCommand, out var body))
        {
            request = default;
            return false;
        }

        if (body.Length < 1)
        {
            request = default;
            return false;
        }

        var ticketLength = body[0];
        if (body.Length < 1 + ticketLength)
        {
            request = default;
            return false;
        }

        var ticket = Encoding.ASCII.GetString(body.Slice(1, ticketLength));
        if (string.IsNullOrEmpty(ticket))
        {
            request = default;
            return false;
        }

        request = new ShipJoinRequest(ticket);
        return true;
    }

    public static byte[] WriteJoinRequest(ShipJoinRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Ticket);

        var ticketBytes = Encoding.ASCII.GetBytes(request.Ticket);
        if (ticketBytes.Length > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Ticket must fit in a single byte length field.");
        }

        var totalLength = 4 + 1 + ticketBytes.Length;
        var buffer = new byte[totalLength];

        WriteHeader(buffer, JoinRequestCommand, (ushort)totalLength);
        buffer[4] = (byte)ticketBytes.Length;
        ticketBytes.CopyTo(buffer.AsSpan(5));

        return buffer;
    }

    public static bool TryReadJoinAck(ReadOnlySpan<byte> frame, out ShipJoinAck ack)
    {
        if (!TryGetBody(frame, JoinAckCommand, out var body))
        {
            ack = default;
            return false;
        }

        if (body.Length < 2)
        {
            ack = default;
            return false;
        }

        var resultCode = body[0];
        var messageLength = body[1];
        if (body.Length < 2 + messageLength)
        {
            ack = default;
            return false;
        }

        var message = Encoding.ASCII.GetString(body.Slice(2, messageLength));
        ack = new ShipJoinAck(resultCode, message);
        return true;
    }

    public static byte[] WriteJoinAck(ShipJoinAck ack)
    {
        ArgumentNullException.ThrowIfNull(ack.Message);

        var messageBytes = Encoding.ASCII.GetBytes(ack.Message);
        if (messageBytes.Length > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(ack), "Ack message must fit in a single byte length field.");
        }

        var totalLength = 4 + 2 + messageBytes.Length;
        var buffer = new byte[totalLength];

        WriteHeader(buffer, JoinAckCommand, (ushort)totalLength);
        buffer[4] = ack.ResultCode;
        buffer[5] = (byte)messageBytes.Length;
        messageBytes.CopyTo(buffer.AsSpan(6));

        return buffer;
    }

    public static bool TryReadPing(ReadOnlySpan<byte> frame, out ShipPing ping)
    {
        if (!TryGetBody(frame, PingCommand, out var body))
        {
            ping = default;
            return false;
        }

        if (body.Length < sizeof(uint))
        {
            ping = default;
            return false;
        }

        var seq = BinaryPrimitives.ReadUInt32LittleEndian(body);
        ping = new ShipPing(seq);
        return true;
    }

    public static byte[] WritePing(ShipPing ping)
    {
        var buffer = new byte[4 + sizeof(uint)];
        WriteHeader(buffer, PingCommand, (ushort)buffer.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), ping.Seq);
        return buffer;
    }

    public static bool TryReadPong(ReadOnlySpan<byte> frame, out ShipPong pong)
    {
        if (!TryGetBody(frame, PongCommand, out var body))
        {
            pong = default;
            return false;
        }

        if (body.Length < sizeof(uint))
        {
            pong = default;
            return false;
        }

        var seq = BinaryPrimitives.ReadUInt32LittleEndian(body);
        pong = new ShipPong(seq);
        return true;
    }

    public static byte[] WritePong(ShipPong pong)
    {
        var buffer = new byte[4 + sizeof(uint)];
        WriteHeader(buffer, PongCommand, (ushort)buffer.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), pong.Seq);
        return buffer;
    }

    private static bool TryGetBody(ReadOnlySpan<byte> frame, byte command, out ReadOnlySpan<byte> body)
    {
        body = default;
        if (frame.Length < 4)
        {
            return false;
        }

        var declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(frame);
        if (declaredLength != frame.Length)
        {
            return false;
        }

        if (frame[2] != command)
        {
            return false;
        }

        body = frame.Slice(4);
        return true;
    }

    private static void WriteHeader(Span<byte> destination, byte command, ushort length)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), length);
        destination[2] = command;
        destination[3] = 0x00; // flags cleared for ship handshake frames
    }
}

public readonly struct ShipJoinRequest
{
    public ShipJoinRequest(string ticket)
    {
        Ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));
    }

    public string Ticket { get; }
}

public readonly struct ShipJoinAck
{
    public ShipJoinAck(byte resultCode, string message)
    {
        ResultCode = resultCode;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public byte ResultCode { get; }

    public string Message { get; }
}

public readonly struct ShipPing
{
    public ShipPing(uint seq)
    {
        Seq = seq;
    }

    public uint Seq { get; }
}

public readonly struct ShipPong
{
    public ShipPong(uint seq)
    {
        Seq = seq;
    }

    public uint Seq { get; }
}
