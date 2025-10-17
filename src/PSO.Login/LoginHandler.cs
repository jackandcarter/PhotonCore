using System.Linq;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PSO.Auth;
using PSO.Proto;

namespace PSO.Login;

public sealed class LoginHandler
{
    private readonly Func<Task<ILoginDatabase>> _dbFactory;
    private readonly HttpClient _adminApiClient;
    private readonly ILogger<LoginHandler> _logger;
    private readonly Func<bool, Task> _reportMetricsAsync;

    public LoginHandler(
        Func<Task<ILoginDatabase>> dbFactory,
        HttpClient adminApiClient,
        ILogger<LoginHandler> logger,
        Func<bool, Task> reportMetricsAsync)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _adminApiClient = adminApiClient ?? throw new ArgumentNullException(nameof(adminApiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reportMetricsAsync = reportMetricsAsync ?? throw new ArgumentNullException(nameof(reportMetricsAsync));
    }

    public async Task<LoginProcessResult> ProcessAsync(ClientHello hello, CancellationToken cancellationToken = default)
    {
        if (hello.Username is null)
        {
            throw new ArgumentNullException(nameof(hello.Username));
        }

        ILoginDatabase? db = null;
        try
        {
            db = await _dbFactory();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open database connection for {Username}", hello.Username);
            await _reportMetricsAsync(false);
            return LoginProcessResult.Failed();
        }

        await using (db)
        {
            var isValid = await VerifyCredentialsAsync(db, hello);
            await _reportMetricsAsync(isValid);

            if (!isValid)
            {
                return LoginProcessResult.Failed();
            }

            var worldList = await FetchWorldListAsync(cancellationToken);
            return LoginProcessResult.Success(worldList);
        }
    }

    private async Task<bool> VerifyCredentialsAsync(ILoginDatabase db, ClientHello hello)
    {
        try
        {
            var result = await db.VerifyPasswordAsync(hello.Username, hello.Password);
            if (!result)
            {
                _logger.LogInformation("Login rejected for {Username}", hello.Username);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error for {Username}", hello.Username);
            return false;
        }
    }

    private async Task<WorldList> FetchWorldListAsync(CancellationToken cancellationToken)
    {
        try
        {
            var httpResponse = await _adminApiClient.GetAsync("/v1/worlds", cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            var payload = await httpResponse.Content.ReadFromJsonAsync<WorldListEnvelope>(cancellationToken: cancellationToken);
            var entries = payload?.Worlds?.Select(world =>
                    new WorldEntry(world.Name, world.Address, (ushort)world.Port))
                .ToArray() ?? Array.Empty<WorldEntry>();

            _logger.LogInformation("Fetched {Count} worlds from registry", entries.Length);
            return new WorldList(entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch world list");
            return new WorldList(Array.Empty<WorldEntry>());
        }
    }
}

public sealed record LoginProcessResult(AuthResponse AuthResponse, WorldList WorldList)
{
    public static LoginProcessResult Success(WorldList worlds)
        => new(new AuthResponse(true, "ok"), worlds);

    public static LoginProcessResult Failed()
        => new(new AuthResponse(false, "invalid"), new WorldList(Array.Empty<WorldEntry>()));
}

internal sealed record WorldListEnvelope(WorldSummary[] Worlds);

internal sealed record WorldSummary(string Name, string Address, int Port);
