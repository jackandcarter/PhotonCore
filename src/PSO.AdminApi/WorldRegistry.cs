using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PSO.AdminApi;

public sealed class WorldRegistryService
{
    private readonly ConcurrentDictionary<string, WorldRegistrationEntry> _worlds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _entryTtl;
    private readonly Func<DateTimeOffset> _clock;

    public WorldRegistryService(IOptions<WorldRegistryOptions> options)
        : this(options, static () => DateTimeOffset.UtcNow)
    {
    }

    internal WorldRegistryService(IOptions<WorldRegistryOptions> options, Func<DateTimeOffset> clock)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (clock is null)
        {
            throw new ArgumentNullException(nameof(clock));
        }

        _entryTtl = options.Value.EntryTtl <= TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(nameof(options), "Entry TTL must be positive.")
            : options.Value.EntryTtl;
        _clock = clock;
    }

    public WorldRegistrationEntry Register(WorldRegistrationRequest request, DateTimeOffset? timestamp = null)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var name = request.Name?.Trim();
        var address = request.Address?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("World name is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("World address is required.", nameof(request));
        }

        if (request.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Port, "World port must be between 1 and 65535.");
        }

        var recordedAt = timestamp ?? _clock();
        var descriptor = new WorldRegistrationEntry(name, address, request.Port, recordedAt);

        _worlds.AddOrUpdate(name, descriptor, static (_, _) => descriptor);
        return descriptor;
    }

    public IReadOnlyCollection<WorldRegistrationEntry> GetActiveWorlds(DateTimeOffset? timestamp = null)
    {
        var now = timestamp ?? _clock();
        var threshold = now - _entryTtl;

        foreach (var pair in _worlds)
        {
            if (pair.Value.LastSeenUtc < threshold)
            {
                _worlds.TryRemove(pair.Key, out _);
            }
        }

        return _worlds.Values
            .Where(world => world.LastSeenUtc >= threshold)
            .OrderBy(world => world.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class WorldRegistryOptions
{
    public TimeSpan EntryTtl { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed record WorldRegistrationRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("port")] int Port);

public sealed record WorldRegistrationEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("lastSeenUtc")] DateTimeOffset LastSeenUtc);

public sealed record WorldRegistrationResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("world")] WorldRegistrationEntry World);

public sealed record WorldListResponse(
    [property: JsonPropertyName("worlds")] IReadOnlyCollection<WorldRegistrationEntry> Worlds);
