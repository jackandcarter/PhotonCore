using System.Linq;
using System.Text.Json;
using PSO.AdminApi;
using Xunit;

namespace PhotonCore.Tests;

public class WorldRegistryTests
{
    [Fact]
    public void Register_AddsAndUpdatesWorldEntries()
    {
        var name = $"World-{Guid.NewGuid():N}";
        var initialRequest = new WorldRegistrationRequest(name, "127.0.0.1", 12001);
        var initialTimestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var firstEntry = WorldRegistry.Register(initialRequest, initialTimestamp);
        Assert.Equal(initialTimestamp, firstEntry.RegisteredAt);

        var updatedRequest = initialRequest with { Port = 13000 };
        var updatedTimestamp = initialTimestamp.AddMinutes(5);
        var updatedEntry = WorldRegistry.Register(updatedRequest, updatedTimestamp);

        Assert.Equal(updatedRequest.Port, updatedEntry.Port);
        Assert.Equal(updatedTimestamp, updatedEntry.RegisteredAt);

        var stored = WorldRegistry.GetAll().Single(world => world.Name == name);
        Assert.Equal(updatedEntry, stored);
    }

    [Fact]
    public void WorldModels_SerializeWithExpectedPropertyNames()
    {
        var request = new WorldRegistrationRequest("World-1", "127.0.0.1", 12001);
        var descriptor = new WorldDescriptor(request.Name, request.Address, request.Port, DateTimeOffset.UnixEpoch);
        var response = new WorldListResponse(new[] { descriptor });

        var requestJson = JsonSerializer.Serialize(request);
        var descriptorJson = JsonSerializer.Serialize(descriptor);
        var responseJson = JsonSerializer.Serialize(response);

        Assert.Contains("\"name\"", requestJson);
        Assert.Contains("\"address\"", requestJson);
        Assert.Contains("\"port\"", requestJson);

        Assert.Contains("\"registeredAt\"", descriptorJson);
        Assert.Contains("\"worlds\"", responseJson);
        Assert.Contains("\"name\"", responseJson);
    }
}
