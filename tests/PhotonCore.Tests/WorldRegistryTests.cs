using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PSO.AdminApi;
using Xunit;

namespace PhotonCore.Tests;

public class WorldRegistryTests
{
    [Fact]
    public void Register_AddsAndUpdatesWorldEntries()
    {
        var options = Options.Create(new WorldRegistryOptions { EntryTtl = TimeSpan.FromSeconds(30) });
        var registry = new WorldRegistryService(options);
        var name = $"World-{Guid.NewGuid():N}";
        var initialRequest = new WorldRegistrationRequest(name, "127.0.0.1", 12001);
        var initialTimestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var firstEntry = registry.Register(initialRequest, initialTimestamp);
        Assert.Equal(initialTimestamp, firstEntry.LastSeenUtc);

        var stored = registry.GetActiveWorlds(initialTimestamp).Single(world => world.Name == name);
        Assert.Equal(firstEntry, stored);

        var updatedRequest = initialRequest with { Port = 13000 };
        var updatedTimestamp = initialTimestamp.AddMinutes(5);
        var updatedEntry = registry.Register(updatedRequest, updatedTimestamp);

        Assert.Equal(updatedRequest.Port, updatedEntry.Port);
        Assert.Equal(updatedTimestamp, updatedEntry.LastSeenUtc);

        var snapshot = registry.GetActiveWorlds(updatedTimestamp).Single(world => world.Name == name);
        Assert.Equal(updatedEntry, snapshot);
    }

    [Fact]
    public void GetActiveWorlds_ExpiresEntriesBeyondTtl()
    {
        var options = Options.Create(new WorldRegistryOptions { EntryTtl = TimeSpan.FromSeconds(30) });
        var registry = new WorldRegistryService(options);
        var timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var request = new WorldRegistrationRequest("World-1", "127.0.0.1", 12001);

        registry.Register(request, timestamp);
        Assert.Single(registry.GetActiveWorlds(timestamp));

        var expiredSnapshot = registry.GetActiveWorlds(timestamp.AddSeconds(31));
        Assert.Empty(expiredSnapshot);
    }

    [Fact]
    public void WorldModels_SerializeWithExpectedPropertyNames()
    {
        var request = new WorldRegistrationRequest("World-1", "127.0.0.1", 12001);
        var entry = new WorldRegistrationEntry(request.Name, request.Address, request.Port, DateTimeOffset.UnixEpoch);
        var response = new WorldListResponse(new[] { entry });

        var requestJson = JsonSerializer.Serialize(request);
        var entryJson = JsonSerializer.Serialize(entry);
        var responseJson = JsonSerializer.Serialize(response);

        Assert.Contains("\"name\"", requestJson);
        Assert.Contains("\"address\"", requestJson);
        Assert.Contains("\"port\"", requestJson);

        Assert.Contains("\"lastSeenUtc\"", entryJson);
        Assert.Contains("\"worlds\"", responseJson);
        Assert.Contains("\"name\"", responseJson);
    }
}
