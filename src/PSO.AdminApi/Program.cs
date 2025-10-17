using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PSO.AdminApi;
using PSO.Auth;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ ";
});

builder.Services.AddSingleton<LoginMetrics>();
builder.Services.Configure<WorldRegistryOptions>(builder.Configuration.GetSection("WorldRegistry"));
builder.Services.AddSingleton<WorldRegistryService>();
builder.Services.AddSingleton(async (_) =>
{
    var cs = builder.Configuration.GetConnectionString("Default")!;
    var db = new Db(cs);
    await db.OpenAsync();
    return db;
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));
var app = builder.Build();
app.UseCors();

app.MapGet("/healthz", async (Task<Db> dbTask, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Healthz");
    var db = await dbTask;
    var ok = await db.PingAsync();
    logger.LogInformation("Health probe responded with {Status}", ok == 1 ? "ok" : "error");
    return Results.Json(new { ok = ok == 1 });
});

app.MapPost("/v1/accounts", async (Task<Db> dbTask, CreateAccount req, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Accounts");
    var db = await dbTask;
    var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
    var acct = await db.CreateAccountAsync(req.Username, hash);
    logger.LogInformation("Created account for {Username}", req.Username);
    return Results.Json(new { status = "created", id = acct.Id, username = acct.Username });
});

app.MapPost("/v1/worlds/register", (WorldRegistryService registry, WorldRegistrationRequest request, ILogger<WorldRegistryService> logger) =>
{
    try
    {
        var world = registry.Register(request);
        logger.LogInformation("Registered world {World} at {Address}:{Port}", world.Name, world.Address, world.Port);
        return Results.Json(new WorldRegistrationResponse("registered", world));
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning(ex, "Invalid registration request for {World}", request.Name);
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (ArgumentOutOfRangeException ex)
    {
        logger.LogWarning(ex, "Registration out of range for {World}", request.Name);
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/v1/worlds", (WorldRegistryService registry, ILogger<WorldRegistryService> logger) =>
{
    var worlds = registry.GetActiveWorlds();
    logger.LogInformation("Serving world list with {Count} entries", worlds.Count);
    return Results.Json(new WorldListResponse(worlds));
});

app.MapPost("/v1/metrics/logins", (LoginMetrics metrics, LoginAttempt attempt, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("Metrics");
    if (attempt.Success)
    {
        metrics.IncrementSuccess();
        logger.LogInformation("Recorded successful login attempt");
    }
    else
    {
        metrics.IncrementFailure();
        logger.LogInformation("Recorded failed login attempt");
    }

    return Results.Accepted();
});

app.MapGet("/metrics", (LoginMetrics metrics, WorldRegistryService registry) =>
{
    var worlds = registry.GetActiveWorlds();
    var payload = metrics.ToPrometheusPayload(worlds.Count);
    return Results.Text(payload, "text/plain", Encoding.UTF8);
});

app.Run("http://127.0.0.1:5080");
record CreateAccount(string Username, string Password);
record LoginAttempt(bool Success);
