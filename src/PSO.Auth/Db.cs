using System.Data;
using Dapper;
using MySqlConnector;

namespace PSO.Auth;
public sealed class Db : IAsyncDisposable
{
    private readonly MySqlConnection _conn;
    public Db(string cs) => _conn = new MySqlConnection(cs);
    public async Task OpenAsync() => await _conn.OpenAsync();
    public async ValueTask DisposeAsync() => await _conn.DisposeAsync();

    public async Task<Account> CreateAccountAsync(string username, string passwordHash)
    {
        var acct = new Account(Guid.NewGuid(), username, passwordHash, DateTimeOffset.UtcNow);
        const string sql = @"INSERT INTO accounts (id, username, password_hash, flags) VALUES (@id, @username, @password_hash, JSON_OBJECT())";
        await _conn.ExecuteAsync(sql, new { id = acct.Id.ToString(), username = acct.Username, password_hash = acct.PasswordHash });
        return acct;
    }
    public async Task<Account?> GetByUsernameAsync(string username)
        => await _conn.QueryFirstOrDefaultAsync<Account?>(@"SELECT id, username, password_hash, created_at FROM accounts WHERE username=@u LIMIT 1",
                                                         new { u = username });

    public async Task<bool> VerifyPasswordAsync(string username, string password)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        const string sql = "SELECT password_hash FROM accounts WHERE username=@u LIMIT 1";
        var hash = await _conn.ExecuteScalarAsync<string?>(sql, new { u = username });

        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        return BCrypt.Net.BCrypt.Verify(password, hash);
    }

    // Helper for quick health check
    public async Task<int> PingAsync() {
        var v = await _conn.ExecuteScalarAsync<string>("SELECT VERSION()");
        return string.IsNullOrEmpty(v) ? 0 : 1;
    }
}
public record Account(Guid Id, string Username, string PasswordHash, DateTimeOffset CreatedAt);
