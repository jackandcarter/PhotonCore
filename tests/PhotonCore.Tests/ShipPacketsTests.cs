// SPDX-License-Identifier: Apache-2.0
using System.Text;
using PSO.Proto;
using Xunit;

namespace PhotonCore.Tests;

public class ShipPacketsTests
{
    [Fact]
    public void ShipJoinRequest_RoundTrips()
    {
        var request = new ShipJoinRequest("ticket-data");
        var encoded = request.Write();
        var decoded = ShipJoinRequest.Read(encoded);

        Assert.Equal(request.Ticket, decoded.Ticket);
    }

    [Fact]
    public void ShipJoinResponse_RoundTrips()
    {
        var response = new ShipJoinResponse(true, "welcome");
        var encoded = response.Write();
        var decoded = ShipJoinResponse.Read(encoded);

        Assert.True(decoded.Success);
        Assert.Equal("welcome", decoded.Message);
    }

    [Fact]
    public void ShipJoinResponse_EncodesLengthPrefixedMessage()
    {
        var response = new ShipJoinResponse(false, "denied");
        var encoded = response.Write();

        Assert.Equal(0, encoded[0]);
        Assert.Equal(Encoding.UTF8.GetByteCount("denied"), encoded[1]);
        var message = Encoding.UTF8.GetString(encoded, 2, encoded[1]);
        Assert.Equal("denied", message);
    }
}
