// SPDX-License-Identifier: Apache-2.0
using System;
using System.Buffers.Binary;
using System.Text;
using PSO.Proto;
using PSO.Proto.Compat;
using Xunit;

namespace PhotonCore.Tests;

public class PcV2CodecTests
{
    [Fact]
    public void TryReadClientHelloParsesCredentials()
    {
        var frame = new byte[0xB0];
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), (ushort)frame.Length);
        frame[2] = 0x93;

        WriteFixed(frame.AsSpan(4 + 0x18, 0x11), "alice");
        WriteFixed(frame.AsSpan(4 + 0x29, 0x11), "hunter2");

        var parsed = PcV2Codec.TryReadClientHello(frame, out var hello);

        Assert.True(parsed);
        Assert.Equal("alice", hello.Username);
        Assert.Equal("hunter2", hello.Password);
    }

    [Fact]
    public void WriteAuthResponseEncodesSuccessAndFailure()
    {
        var ok = PcV2Codec.WriteAuthResponse(true, "ok");
        var expectedOk = new byte[] { 0x08, 0x00, 0xA1, 0x00, 0x01, 0x02, 0x6F, 0x6B };
        Assert.Equal(expectedOk, ok);

        var fail = PcV2Codec.WriteAuthResponse(false, "invalid");
        var expectedFail = new byte[]
        {
            0x0D, 0x00, 0xA1, 0x00, 0x00, 0x07,
            0x69, 0x6E, 0x76, 0x61, 0x6C, 0x69, 0x64,
        };
        Assert.Equal(expectedFail, fail);
    }

    [Fact]
    public void WriteWorldListProducesPcV2Layout()
    {
        var world = new WorldEntry("World-1", "127.0.0.1", 12001);
        var payload = PcV2Codec.WriteWorldList(new[] { world });

        var expected = new byte[0x2B];
        BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(0, 2), (ushort)expected.Length);
        expected[2] = 0xA2;
        expected[4] = 0x01;
        WriteFixed(expected.AsSpan(5, 0x20), "World-1");
        expected[5 + 0x20 + 0] = 0x7F;
        expected[5 + 0x20 + 1] = 0x00;
        expected[5 + 0x20 + 2] = 0x00;
        expected[5 + 0x20 + 3] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(5 + 0x20 + 4, 2), 12001);

        Assert.Equal(expected, payload);
    }

    private static void WriteFixed(Span<byte> destination, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        var copyLength = Math.Min(bytes.Length, destination.Length - 1);
        bytes.AsSpan(0, copyLength).CopyTo(destination);
        destination[copyLength] = 0x00;
        if (copyLength + 1 < destination.Length)
        {
            destination.Slice(copyLength + 1).Clear();
        }
    }
}
