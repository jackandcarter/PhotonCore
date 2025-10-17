// SPDX-License-Identifier: Apache-2.0
using System;
using System.Buffers.Binary;
using System.Text;

namespace PSO.Proto;

public enum PacketId : ushort
{
    ClientHello = 0x0101,
    AuthResponse = 0x0102,
    WorldList = 0x0103,
}

public readonly struct ClientHello
{
    public ClientHello(string username, string password)
    {
        Username = username ?? throw new ArgumentNullException(nameof(username));
        Password = password ?? throw new ArgumentNullException(nameof(password));
    }

    public string Username { get; }

    public string Password { get; }

    public static ClientHello Read(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < sizeof(ushort))
        {
            throw new ArgumentException("Buffer too small for packet header", nameof(buffer));
        }

        var packetId = BinaryPrimitives.ReadUInt16BigEndian(buffer);
        if (packetId != (ushort)PacketId.ClientHello)
        {
            throw new InvalidOperationException("Unexpected packet identifier.");
        }

        var index = sizeof(ushort);
        if (index >= buffer.Length)
        {
            throw new ArgumentException("Buffer too small for username length", nameof(buffer));
        }

        var usernameLength = buffer[index++];
        if (index + usernameLength > buffer.Length)
        {
            throw new ArgumentException("Buffer too small for username content", nameof(buffer));
        }

        var username = Encoding.UTF8.GetString(buffer.Slice(index, usernameLength));
        index += usernameLength;

        if (index >= buffer.Length)
        {
            throw new ArgumentException("Buffer too small for password length", nameof(buffer));
        }

        var passwordLength = buffer[index++];
        if (index + passwordLength > buffer.Length)
        {
            throw new ArgumentException("Buffer too small for password content", nameof(buffer));
        }

        var password = Encoding.UTF8.GetString(buffer.Slice(index, passwordLength));

        return new ClientHello(username, password);
    }

    public byte[] Write()
    {
        var usernameBytes = Encoding.UTF8.GetBytes(Username);
        var passwordBytes = Encoding.UTF8.GetBytes(Password);

        if (usernameBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Username too long to encode.");
        }

        if (passwordBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Password too long to encode.");
        }

        var buffer = new byte[sizeof(ushort) + 1 + usernameBytes.Length + 1 + passwordBytes.Length];
        var index = 0;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(index, sizeof(ushort)), (ushort)PacketId.ClientHello);
        index += sizeof(ushort);

        buffer[index++] = (byte)usernameBytes.Length;
        usernameBytes.CopyTo(buffer.AsSpan(index, usernameBytes.Length));
        index += usernameBytes.Length;

        buffer[index++] = (byte)passwordBytes.Length;
        passwordBytes.CopyTo(buffer.AsSpan(index, passwordBytes.Length));

        return buffer;
    }
}

public readonly struct AuthResponse
{
    public AuthResponse(bool success, string message)
    {
        Success = success;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public bool Success { get; }

    public string Message { get; }

    public byte[] Write()
    {
        var messageBytes = Encoding.UTF8.GetBytes(Message);
        if (messageBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Message too long to encode.");
        }

        var buffer = new byte[sizeof(ushort) + 1 + 1 + messageBytes.Length];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)PacketId.AuthResponse);

        span[sizeof(ushort)] = Success ? (byte)1 : (byte)0;
        span[sizeof(ushort) + 1] = (byte)messageBytes.Length;
        messageBytes.CopyTo(span.Slice(sizeof(ushort) + 2));

        return buffer;
    }
}

public readonly struct WorldEntry
{
    public WorldEntry(string name, string address, ushort port)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Address = address ?? throw new ArgumentNullException(nameof(address));
        Port = port;
    }

    public string Name { get; }

    public string Address { get; }

    public ushort Port { get; }

    public void Write(Span<byte> destination, ref int index)
    {
        var nameBytes = Encoding.UTF8.GetBytes(Name);
        var addressBytes = Encoding.UTF8.GetBytes(Address);

        if (nameBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("World name too long to encode.");
        }

        if (addressBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("World address too long to encode.");
        }

        if (index + 1 + nameBytes.Length + 1 + addressBytes.Length + sizeof(ushort) > destination.Length)
        {
            throw new ArgumentException("Destination span too small for world entry", nameof(destination));
        }

        destination[index++] = (byte)nameBytes.Length;
        nameBytes.CopyTo(destination.Slice(index, nameBytes.Length));
        index += nameBytes.Length;

        destination[index++] = (byte)addressBytes.Length;
        addressBytes.CopyTo(destination.Slice(index, addressBytes.Length));
        index += addressBytes.Length;

        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(index, sizeof(ushort)), Port);
        index += sizeof(ushort);
    }
}

public readonly struct WorldList
{
    public WorldList(WorldEntry[] worlds)
    {
        Worlds = worlds ?? throw new ArgumentNullException(nameof(worlds));
    }

    public WorldEntry[] Worlds { get; }

    public byte[] Write()
    {
        if (Worlds.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Too many worlds to encode.");
        }

        var totalSize = sizeof(ushort) + 1;
        foreach (var world in Worlds)
        {
            var nameLength = Encoding.UTF8.GetByteCount(world.Name);
            var addressLength = Encoding.UTF8.GetByteCount(world.Address);
            if (nameLength > byte.MaxValue)
            {
                throw new InvalidOperationException("World name too long to encode.");
            }

            if (addressLength > byte.MaxValue)
            {
                throw new InvalidOperationException("World address too long to encode.");
            }

            totalSize += 1 + nameLength + 1 + addressLength + sizeof(ushort);
        }

        var buffer = new byte[totalSize];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)PacketId.WorldList);
        span[sizeof(ushort)] = (byte)Worlds.Length;

        var index = sizeof(ushort) + 1;
        foreach (var world in Worlds)
        {
            world.Write(span, ref index);
        }

        return buffer;
    }
}
