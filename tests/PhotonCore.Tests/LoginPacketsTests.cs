// SPDX-License-Identifier: Apache-2.0
using System.Linq;
using System.Text;
using PSO.Proto;
using Xunit;

namespace PhotonCore.Tests;

public class LoginPacketsTests
{
    [Fact]
    public void ClientHello_RoundTrip_Succeeds()
    {
        var packet = new ClientHello("user", "secret");
        var encoded = packet.Write();

        var decoded = ClientHello.Read(encoded);

        Assert.Equal(packet.Username, decoded.Username);
        Assert.Equal(packet.Password, decoded.Password);
    }

    [Fact]
    public void AuthResponse_Write_ProducesExpectedLayout()
    {
        var packet = new AuthResponse(true, "Welcome");

        var encoded = packet.Write();

        var expected = new byte[]
        {
            0x01, 0x02, // PacketId.AuthResponse
            0x01,       // Success flag
            0x07,       // Message length
        }
        .Concat(Encoding.UTF8.GetBytes("Welcome"))
        .ToArray();

        Assert.Equal(expected, encoded);
    }

    [Fact]
    public void WorldList_Write_ProducesExpectedLayout()
    {
        var packet = new WorldList(
            new[]
            {
                new WorldEntry("Earth", "127.0.0.1", 1234),
                new WorldEntry("Mars", "192.168.0.1", 4321),
            });

        var encoded = packet.Write();

        var expectedPrefix = new byte[]
        {
            0x01, 0x03, // PacketId.WorldList
            0x02,       // Count
        };

        Assert.True(encoded.AsSpan().Slice(0, expectedPrefix.Length).SequenceEqual(expectedPrefix));

        var index = expectedPrefix.Length;

        index = AssertWorldEntry(encoded, index, "Earth", "127.0.0.1", 1234);
        index = AssertWorldEntry(encoded, index, "Mars", "192.168.0.1", 4321);

        Assert.Equal(encoded.Length, index);
    }

    private static int AssertWorldEntry(byte[] encoded, int index, string name, string address, ushort port)
    {
        var nameLength = encoded[index++];
        Assert.Equal(Encoding.UTF8.GetByteCount(name), nameLength);
        Assert.Equal(name, Encoding.UTF8.GetString(encoded, index, nameLength));
        index += nameLength;

        var addressLength = encoded[index++];
        Assert.Equal(Encoding.UTF8.GetByteCount(address), addressLength);
        Assert.Equal(address, Encoding.UTF8.GetString(encoded, index, addressLength));
        index += addressLength;

        var encodedPort = (encoded[index] << 8) | encoded[index + 1];
        Assert.Equal(port, (ushort)encodedPort);
        index += 2;

        return index;
    }
}
