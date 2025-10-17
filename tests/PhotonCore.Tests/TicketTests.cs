// SPDX-License-Identifier: Apache-2.0
using System.Text;
using PSO.Auth;
using Xunit;

namespace PhotonCore.Tests;

public class TicketTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef");

    [Fact]
    public void IssueAndValidate_ReturnsOriginalAccountId()
    {
        var clock = new TestClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var tickets = new Tickets(Secret, TimeSpan.FromSeconds(60), clock.UtcNow);
        var accountId = Guid.NewGuid();

        var (token, expiresAt) = tickets.Issue(accountId, TimeSpan.FromSeconds(30));

        Assert.True(tickets.TryValidate(token, out var parsedId));
        Assert.Equal(accountId, parsedId);
        Assert.Equal(clock.UtcNow().AddSeconds(30), expiresAt);
    }

    [Fact]
    public void TryValidate_Fails_WhenSignatureTampered()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var tickets = new Tickets(Secret, TimeSpan.FromSeconds(60), clock.UtcNow);
        var (token, _) = tickets.Issue(Guid.NewGuid());

        var tampered = token[..^1] + (token[^1] == 'A' ? "B" : "A");

        Assert.False(tickets.TryValidate(tampered, out _));
    }

    [Fact]
    public void TryValidate_Fails_WhenExpired()
    {
        var clock = new TestClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var tickets = new Tickets(Secret, TimeSpan.FromSeconds(60), clock.UtcNow);
        var (token, _) = tickets.Issue(Guid.NewGuid(), TimeSpan.FromSeconds(2));

        clock.Advance(TimeSpan.FromSeconds(3));

        Assert.False(tickets.TryValidate(token, out _));
    }

    private sealed class TestClock
    {
        private DateTimeOffset _now;

        public TestClock(DateTimeOffset initial)
        {
            _now = initial;
        }

        public DateTimeOffset UtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
