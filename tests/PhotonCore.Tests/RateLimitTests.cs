using System;
using System.Net;
using PSO.Login;
using Xunit;

namespace PhotonCore.Tests;

public class RateLimitTests
{
    [Fact]
    public void RepeatedFailuresTriggerLimiter()
    {
        var limiter = new LoginRateLimiter(5, TimeSpan.FromSeconds(60));
        var address = IPAddress.Parse("192.0.2.10");
        const string username = "user";
        var start = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var timestamp = start.AddSeconds(attempt * 5);
            Assert.False(limiter.IsThrottled(address, username, timestamp));
            limiter.RecordFailure(address, username, timestamp);
        }

        Assert.True(limiter.IsThrottled(address, username, start.AddSeconds(25)));
    }

    [Fact]
    public void ResetClearsLimiter()
    {
        var limiter = new LoginRateLimiter(5, TimeSpan.FromSeconds(60));
        var address = IPAddress.Loopback;
        const string username = "user";
        var timestamp = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            limiter.RecordFailure(address, username, timestamp);
        }

        Assert.True(limiter.IsThrottled(address, username, timestamp));

        limiter.Reset(address, username);

        Assert.False(limiter.IsThrottled(address, username, timestamp));
    }
}
