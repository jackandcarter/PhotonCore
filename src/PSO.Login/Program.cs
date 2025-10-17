using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using PSO.Auth;
using PSO.Login;
using PSO.Net;
using PSO.Proto;
using PSO.Proto.Compat;

var bind = Environment.GetEnvironmentVariable("PSO_LOGIN_BIND") ?? "127.0.0.1:12000";
var parts = bind.Split(':');
var ep = new IPEndPoint(IPAddress.Parse(parts[0]), int.Parse(parts[1]));

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Information);
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ ";
    });
});

var logger = loggerFactory.CreateLogger("PSO.Login");

var listener = new TcpListener(ep);
listener.Start();
logger.LogInformation("Login server listening on {Bind}", bind);

var connectionString = Environment.GetEnvironmentVariable("PCORE_DB")
                      ?? "server=127.0.0.1;port=3306;user=psoapp;password=psopass;database=pso;";

var adminApiUrl = Environment.GetEnvironmentVariable("PCORE_ADMIN_URL") ?? "http://127.0.0.1:5080";
var adminApiClient = new HttpClient { BaseAddress = new Uri(adminApiUrl) };

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    var remote = client.Client.RemoteEndPoint;
    _ = Task.Run(async () =>
    {
        try
        {
            await HandleClientAsync(client);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error processing client {Endpoint}", remote);
            client.Close();
        }
    });
}

async Task<ILoginDatabase> CreateDatabaseAsync()
{
    var db = new Db(connectionString);
    await db.OpenAsync();
    return db;
}

async Task ReportLoginMetricsAsync(bool success)
{
    try
    {
        var response = await adminApiClient.PostAsJsonAsync("/v1/metrics/logins", new LoginAttemptDto(success));
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Admin API rejected metrics update with status {Status}", response.StatusCode);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to report login metrics");
    }
}

/// <summary>
/// Handles the login handshake: banner, ClientHello parsing, authentication, and world list reply.
/// </summary>
async Task HandleClientAsync(TcpClient client)
{
    using var ns = client.GetStream();
    await TcpHelpers.WriteFrameAsync(ns, Encoding.UTF8.GetBytes(TcpHelpers.Banner("LOGIN")));

    var frame = await TcpHelpers.ReadFrameAsync(ns, format: FrameFormat.PcV2);
    if (frame is not { Length: > 0 })
    {
        logger.LogWarning("Received empty payload from {Endpoint}", client.Client.RemoteEndPoint);
        client.Close();
        return;
    }

    if (!PcV2Codec.TryReadClientHello(frame, out var hello))
    {
        logger.LogWarning("Invalid PC v2 client hello received from {Endpoint}", client.Client.RemoteEndPoint);
        client.Close();
        return;
    }

    logger.LogInformation("Processing PC v2 login for {Username}", hello.Username);

    bool isValid;
    try
    {
        await using var db = await CreateDatabaseAsync();
        isValid = await db.VerifyPasswordAsync(hello.Username, hello.Password);
        if (!isValid)
        {
            logger.LogInformation("Login rejected for {Username}", hello.Username);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Authentication error for {Username}", hello.Username);
        isValid = false;
    }

    await ReportLoginMetricsAsync(isValid);

    await TcpHelpers.WriteFrameAsync(ns, PcV2Codec.WriteAuthResponse(isValid, isValid ? "ok" : "invalid"), format: FrameFormat.PcV2);

    if (!isValid)
    {
        client.Close();
        return;
    }

    var worldList = PcV2Codec.WriteWorldList(new[]
    {
        new WorldEntry("World-1", "127.0.0.1", 12001),
    });

    await TcpHelpers.WriteFrameAsync(ns, worldList, format: FrameFormat.PcV2);
    client.Close();
}

record LoginAttemptDto(bool Success);
