using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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

const int MaxUsernameLength = 32;
const int MaxPasswordLength = 64;
var listener = new TcpListener(ep);
listener.Start();
logger.LogInformation("Login server listening on {Bind}", bind);

var connectionString = Environment.GetEnvironmentVariable("PCORE_DB")
                      ?? "server=127.0.0.1;port=3306;user=psoapp;password=psopass;database=pso;";

var adminApiUrl = Environment.GetEnvironmentVariable("PCORE_ADMIN_URL") ?? "http://127.0.0.1:5080";
var adminApiClient = new HttpClient { BaseAddress = new Uri(adminApiUrl) };
var rateLimiter = new LoginRateLimiter(5, TimeSpan.FromSeconds(60));

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    var remote = client.Client.RemoteEndPoint;
    logger.LogInformation("Client connected {Endpoint}", remote);
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

    using var readTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    byte[]? frame;
    try
    {
        frame = await TcpHelpers.ReadFrameAsync(ns, readTimeoutCts.Token, FrameFormat.PcV2);
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("Timed out waiting for client hello from {Endpoint}", client.Client.RemoteEndPoint);
        client.Close();
        return;
    }
    catch (TcpHelpers.FrameTooLargeException ex)
    {
        logger.LogWarning(ex, "Frame exceeded limit from {Endpoint}", client.Client.RemoteEndPoint);
        client.Close();
        return;
    }

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

    logger.LogInformation("Parsed client hello for {Username} from {Endpoint}", hello.Username, client.Client.RemoteEndPoint);

    var usernameForAuth = hello.Username.Length > MaxUsernameLength
        ? hello.Username[..MaxUsernameLength]
        : hello.Username;
    var passwordForAuth = hello.Password.Length > MaxPasswordLength
        ? hello.Password[..MaxPasswordLength]
        : hello.Password;

    var remoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
    var now = DateTimeOffset.UtcNow;
    if (rateLimiter.IsThrottled(remoteAddress, usernameForAuth, now))
    {
        logger.LogWarning("Throttled login attempt for {Username} from {Endpoint}", usernameForAuth, client.Client.RemoteEndPoint);
        await TcpHelpers.WriteFrameAsync(ns, PcV2Codec.WriteAuthResponse(false, "invalid"), format: FrameFormat.PcV2);
        client.Close();
        return;
    }

    bool isValid;
    try
    {
        await using var db = await CreateDatabaseAsync();
        isValid = await db.VerifyPasswordAsync(usernameForAuth, passwordForAuth);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Authentication error for {Username}", usernameForAuth);
        isValid = false;
    }

    if (!isValid)
    {
        logger.LogInformation("Authentication failed for {Username} from {Endpoint}", usernameForAuth, client.Client.RemoteEndPoint);
        rateLimiter.RecordFailure(remoteAddress, usernameForAuth, now);
    }
    else
    {
        rateLimiter.Reset(remoteAddress, usernameForAuth);
        logger.LogInformation("Authentication succeeded for {Username} from {Endpoint}", usernameForAuth, client.Client.RemoteEndPoint);
    }

    await ReportLoginMetricsAsync(isValid);

    await TcpHelpers.WriteFrameAsync(ns, PcV2Codec.WriteAuthResponse(isValid, isValid ? "ok" : "invalid"), format: FrameFormat.PcV2);

    if (!isValid)
    {
        client.Close();
        return;
    }

    var worlds = await FetchWorldListAsync(CancellationToken.None);
    var worldListPayload = PcV2Codec.WriteWorldList(worlds);

    await TcpHelpers.WriteFrameAsync(ns, worldListPayload, format: FrameFormat.PcV2);
    client.Close();
}

async Task<WorldEntry[]> FetchWorldListAsync(CancellationToken cancellationToken)
{
    try
    {
        var httpResponse = await adminApiClient.GetAsync("/v1/worlds", cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        var payload = await httpResponse.Content.ReadFromJsonAsync<WorldListEnvelope>(cancellationToken: cancellationToken);
        var entries = payload?.Worlds?.Select(world => new WorldEntry(world.Name, world.Address, (ushort)world.Port)).ToArray()
                      ?? Array.Empty<WorldEntry>();

        logger.LogInformation("Fetched {Count} worlds from registry", entries.Length);
        return entries;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to fetch world list");
        return Array.Empty<WorldEntry>();
    }
}

record LoginAttemptDto(bool Success);
