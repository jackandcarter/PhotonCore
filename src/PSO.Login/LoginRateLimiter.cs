using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;

namespace PSO.Login;

public sealed class LoginRateLimiter
{
    private readonly ConcurrentDictionary<(string RemoteAddress, string Username), SlidingWindow> _attempts = new();

    public LoginRateLimiter(int maxAttempts, TimeSpan window)
    {
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        MaxAttempts = maxAttempts;
        Window = window;
    }

    public int MaxAttempts { get; }

    public TimeSpan Window { get; }

    public bool IsThrottled(IPAddress address, string username, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(username);

        if (!_attempts.TryGetValue(CreateKey(address, username), out var window))
        {
            return false;
        }

        return window.IsThrottled(timestamp, Window, MaxAttempts);
    }

    public void RecordFailure(IPAddress address, string username, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(username);

        var window = _attempts.GetOrAdd(CreateKey(address, username), _ => new SlidingWindow());
        window.Add(timestamp, Window, MaxAttempts);
    }

    public void Reset(IPAddress address, string username)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(username);

        if (_attempts.TryGetValue(CreateKey(address, username), out var window))
        {
            window.Clear();
        }
    }

    private static (string RemoteAddress, string Username) CreateKey(IPAddress address, string username)
        => (address.ToString(), username);

    private sealed class SlidingWindow
    {
        private readonly Queue<DateTimeOffset> _failures = new();
        private readonly object _gate = new();

        public bool IsThrottled(DateTimeOffset timestamp, TimeSpan window, int maxAttempts)
        {
            lock (_gate)
            {
                Trim(timestamp, window);
                return _failures.Count >= maxAttempts;
            }
        }

        public void Add(DateTimeOffset timestamp, TimeSpan window, int maxAttempts)
        {
            lock (_gate)
            {
                Trim(timestamp, window);
                _failures.Enqueue(timestamp);
                if (_failures.Count > maxAttempts * 2)
                {
                    // Prevent unbounded growth if Reset isn't called.
                    Trim(timestamp, window);
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _failures.Clear();
            }
        }

        private void Trim(DateTimeOffset timestamp, TimeSpan window)
        {
            while (_failures.Count > 0 && timestamp - _failures.Peek() >= window)
            {
                _failures.Dequeue();
            }
        }
    }
}
