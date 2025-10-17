// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PSO.Proto.Compat;

public static class PcV2Codec
{
    private const byte ClientHelloCommand = 0x93;
    private const byte AuthResponseCommand = 0xA1;
    private const byte WorldListCommand = 0xA2;

    private const int ClientHelloBodyLength = 0xAC;
    private const int SerialOffset = 0x18;
    private const int AccessKeyOffset = 0x29;
    private const int SerialFieldLength = 0x11;
    private const int AccessKeyFieldLength = 0x11;

    public static bool TryReadClientHello(ReadOnlySpan<byte> frame, out ClientHello hello)
    {
        hello = default;

        if (frame.Length < 4)
        {
            return false;
        }

        var declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(frame);
        if (declaredLength != frame.Length)
        {
            return false;
        }

        if (frame[2] != ClientHelloCommand)
        {
            return false;
        }

        var body = frame.Slice(4);
        if (body.Length < ClientHelloBodyLength)
        {
            return false;
        }

        var username = ReadFixedString(body.Slice(SerialOffset, SerialFieldLength));
        var password = ReadFixedString(body.Slice(AccessKeyOffset, AccessKeyFieldLength));

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        hello = new ClientHello(username, password);
        return true;
    }

    public static byte[] WriteAuthResponse(bool success, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageBytes = Encoding.ASCII.GetBytes(message);
        if (messageBytes.Length > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(message), "PC v2 auth message must fit in one byte length field.");
        }

        var totalLength = 4 + 2 + messageBytes.Length;
        var buffer = new byte[totalLength];

        WriteHeader(buffer, AuthResponseCommand, (ushort)totalLength);
        buffer[4] = success ? (byte)1 : (byte)0;
        buffer[5] = (byte)messageBytes.Length;
        messageBytes.CopyTo(buffer.AsSpan(6));

        return buffer;
    }

    public static byte[] WriteWorldList(IEnumerable<WorldEntry> worlds)
    {
        ArgumentNullException.ThrowIfNull(worlds);

        var entries = worlds.ToArray();
        if (entries.Length > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(worlds), "PC v2 world list supports at most 255 entries.");
        }

        const int nameFieldLength = 0x20;
        const int worldEntryLength = nameFieldLength + 4 + 2;

        var totalLength = 4 + 1 + (entries.Length * worldEntryLength);
        var buffer = new byte[totalLength];

        WriteHeader(buffer, WorldListCommand, (ushort)totalLength);
        buffer[4] = (byte)entries.Length;

        var offset = 5;
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry.Name);
            ArgumentNullException.ThrowIfNull(entry.Address);

            WriteFixedAscii(buffer.AsSpan(offset, nameFieldLength), entry.Name);
            offset += nameFieldLength;

            var addressBytes = ParseIPv4(entry.Address);
            addressBytes.CopyTo(buffer.AsSpan(offset, 4));
            offset += 4;

            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset, 2), entry.Port);
            offset += 2;
        }

        return buffer;
    }

    private static void WriteHeader(Span<byte> destination, byte command, ushort length)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), length);
        destination[2] = command;
        destination[3] = 0x00; // Flag is zero during the login handshake
    }

    private static ReadOnlySpan<byte> ParseIPv4(string address)
    {
        if (!IPAddress.TryParse(address, out var ipAddress) || ipAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("PC v2 world list only supports IPv4 addresses", nameof(address));
        }

        return ipAddress.GetAddressBytes();
    }

    private static void WriteFixedAscii(Span<byte> destination, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        var length = Math.Min(bytes.Length, destination.Length - 1);
        bytes.AsSpan(0, length).CopyTo(destination);
        destination[length] = 0;
        if (length + 1 < destination.Length)
        {
            destination.Slice(length + 1).Clear();
        }
    }

    private static string ReadFixedString(ReadOnlySpan<byte> data)
    {
        var zeroIndex = data.IndexOf((byte)0);
        var slice = zeroIndex >= 0 ? data.Slice(0, zeroIndex) : data;
        return Encoding.ASCII.GetString(slice);
    }
}
