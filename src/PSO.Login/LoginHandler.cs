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
    private readonly Tickets _tickets;

    public LoginHandler(
        Func<Task<ILoginDatabase>> dbFactory,
        HttpClient adminApiClient,
        ILogger<LoginHandler> logger,
        Func<bool, Task> reportMetricsAsync,
        Tickets tickets)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _adminApiClient = adminApiClient ?? throw new ArgumentNullException(nameof(adminApiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reportMetricsAsync = reportMetricsAsync ?? throw new ArgumentNullException(nameof(reportMetricsAsync));
        _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
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
            var account = await AuthenticateAsync(db, hello);
            var isValid = account is not null;
            await _reportMetricsAsync(isValid);

            if (!isValid)
            {
                return LoginProcessResult.Failed();
            }

            var worldList = await FetchWorldListAsync(cancellationToken);
            var (token, _) = _tickets.Issue(account!.Id);
            return LoginProcessResult.Success(worldList, token);
        }
    }

    private async Task<Account?> AuthenticateAsync(ILoginDatabase db, ClientHello hello)
    {
        try
        {
            var account = await db.AuthenticateAsync(hello.Username, hello.Password);
            if (account is null)
            {
                _logger.LogInformation("Login rejected for {Username}", hello.Username);
            }
            return account;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error for {Username}", hello.Username);
            return null;
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

public sealed record LoginProcessResult(AuthResponse AuthResponse, WorldList WorldList, string? SessionTicket)
{
    public static LoginProcessResult Success(WorldList worlds, string ticket)
        => new(new AuthResponse(true, "ok"), worlds, ticket);

    public static LoginProcessResult Failed()
        => new(new AuthResponse(false, "invalid"), new WorldList(Array.Empty<WorldEntry>()), null);
}
