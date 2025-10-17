using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PSO.Auth;

var bind = Environment.GetEnvironmentVariable("PSO_SHIP_BIND") ?? "127.0.0.1:12001";
var parts = bind.Split(':'); var ep = new IPEndPoint(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
var listener = new TcpListener(ep); listener.Start();
Console.WriteLine($"[ship] listening on {bind}");

var adminApiUrl = Environment.GetEnvironmentVariable("PCORE_ADMIN_URL") ?? "http://127.0.0.1:5080";
using var adminApiClient = new HttpClient { BaseAddress = new Uri(adminApiUrl) };
var shipName = Environment.GetEnvironmentVariable("PCORE_SHIP_NAME") ?? "World-1";
var shipAddress = Environment.GetEnvironmentVariable("PCORE_SHIP_ADDR") ?? parts[0];
var shipPortValue = Environment.GetEnvironmentVariable("PCORE_SHIP_PORT");
var shipPort = shipPortValue is { Length: > 0 } ? int.Parse(shipPortValue) : int.Parse(parts[1]);

Tickets tickets;
try
{
    tickets = Tickets.FromEnvironment();
}
catch (Exception ex)
{
    Console.WriteLine($"[ship] missing ticket secret: {ex.Message}");
    return;
}

await RegisterShipAsync(adminApiClient);

_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        await RegisterShipAsync(adminApiClient);
    }
});

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () =>
    {
        try
        {
            await ShipSession.RunAsync(client, tickets, Console.WriteLine);
        }
        catch (Exception ex)
        {
            var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            Console.WriteLine($"[ship] event=close reason=error remote={remote} error=\"{ex.Message}\"");
        }
    });
}

async Task RegisterShipAsync(HttpClient httpClient)
{
    try
    {
        var registration = new ShipRegistration(shipName, shipAddress, shipPort);
        var response = await httpClient.PostAsJsonAsync("/v1/worlds/register", registration);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[ship] failed to register world: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        else
        {
            Console.WriteLine($"[ship] registered as '{shipName}' at {shipAddress}:{shipPort}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ship] registration error: {ex.Message}");
    }
}

internal sealed record ShipRegistration(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("port")] int Port);
