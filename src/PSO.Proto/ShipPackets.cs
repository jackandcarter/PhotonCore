// SPDX-License-Identifier: Apache-2.0
using System.Text;

namespace PSO.Proto;

public readonly struct ShipJoinRequest
{
    public ShipJoinRequest(string ticket)
    {
        Ticket = ticket ?? throw new ArgumentNullException(nameof(ticket));
    }

    public string Ticket { get; }

    public static ShipJoinRequest Read(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 1)
        {
            throw new ArgumentException("Buffer too small for join request", nameof(buffer));
        }

        var ticketLength = buffer[0];
        if (buffer.Length < 1 + ticketLength)
        {
            throw new ArgumentException("Buffer too small for ticket payload", nameof(buffer));
        }

        var ticket = Encoding.UTF8.GetString(buffer.Slice(1, ticketLength));
        return new ShipJoinRequest(ticket);
    }

    public byte[] Write()
    {
        var ticketBytes = Encoding.UTF8.GetBytes(Ticket);
        if (ticketBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Ticket too long to encode.");
        }

        var buffer = new byte[1 + ticketBytes.Length];
        buffer[0] = (byte)ticketBytes.Length;
        ticketBytes.CopyTo(buffer.AsSpan(1));
        return buffer;
    }
}

public readonly struct ShipJoinResponse
{
    public ShipJoinResponse(bool success, string message)
    {
        Success = success;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public bool Success { get; }

    public string Message { get; }

    public static ShipJoinResponse Read(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 2)
        {
            throw new ArgumentException("Buffer too small for join response", nameof(buffer));
        }

        var success = buffer[0] != 0;
        var messageLength = buffer[1];
        if (buffer.Length < 2 + messageLength)
        {
            throw new ArgumentException("Buffer too small for response message", nameof(buffer));
        }

        var message = Encoding.UTF8.GetString(buffer.Slice(2, messageLength));
        return new ShipJoinResponse(success, message);
    }

    public byte[] Write()
    {
        var messageBytes = Encoding.UTF8.GetBytes(Message);
        if (messageBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Message too long to encode.");
        }

        var buffer = new byte[2 + messageBytes.Length];
        buffer[0] = Success ? (byte)1 : (byte)0;
        buffer[1] = (byte)messageBytes.Length;
        messageBytes.CopyTo(buffer.AsSpan(2));
        return buffer;
    }
}
