using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json.Serialization;

namespace PSO.AdminApi;

public static class WorldRegistry
{
    private static readonly ConcurrentDictionary<string, WorldDescriptor> Worlds =
        new(StringComparer.OrdinalIgnoreCase);

    public static WorldDescriptor Register(WorldRegistrationRequest request, DateTimeOffset? timestamp = null)
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

        var recordedAt = timestamp ?? DateTimeOffset.UtcNow;
        var descriptor = new WorldDescriptor(name, address, request.Port, recordedAt);

        Worlds.AddOrUpdate(name, descriptor, static (_, _) => descriptor);
        return descriptor;
    }

    public static IReadOnlyCollection<WorldDescriptor> GetAll()
    {
        return Worlds.Values
            .OrderBy(world => world.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed record WorldRegistrationRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("port")] int Port);

public sealed record WorldDescriptor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("registeredAt")] DateTimeOffset RegisteredAt);

public sealed record WorldRegistrationResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("world")] WorldDescriptor World);

public sealed record WorldListResponse(
    [property: JsonPropertyName("worlds")] IReadOnlyCollection<WorldDescriptor> Worlds);
