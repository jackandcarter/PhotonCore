using System.Net;
using System.Net.Sockets;
using System.Text;
using PSO.Auth;
using PSO.Net;
using PSO.Proto;

var bind = Environment.GetEnvironmentVariable("PSO_LOGIN_BIND") ?? "127.0.0.1:12000";
var parts = bind.Split(':'); var ep = new IPEndPoint(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
var listener = new TcpListener(ep); listener.Start();
Console.WriteLine($"[login] listening on {bind}");

var connectionString = Environment.GetEnvironmentVariable("PCORE_DB")
                      ?? "server=127.0.0.1;port=3306;user=psoapp;password=psopass;database=pso;";

var worlds = new WorldList(
    new[]
    {
        new WorldEntry("World-1", "127.0.0.1", 12001),
    });

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () =>
    {
        await HandleClientAsync(client);
    });
}

/// <summary>
/// Handles the login handshake: banner, ClientHello parsing, authentication, and world list reply.
/// </summary>
async Task HandleClientAsync(TcpClient client)
{
    await using var db = new Db(connectionString);

    try
    {
        await db.OpenAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[login] failed to open database connection: {ex.Message}");
        client.Close();
        return;
    }

    using var ns = client.GetStream();
    await TcpHelpers.WriteFrameAsync(ns, Encoding.UTF8.GetBytes(TcpHelpers.Banner("LOGIN")));

    var payload = await TcpHelpers.ReadFrameAsync(ns);
    if (payload is not { Length: > 0 })
    {
        client.Close();
        return;
    }

    ClientHello hello;
    try
    {
        hello = ClientHello.Read(payload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[login] invalid ClientHello received: {ex.Message}");
        client.Close();
        return;
    }

    bool isValid;
    try
    {
        isValid = await db.VerifyPasswordAsync(hello.Username, hello.Password);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[login] authentication error: {ex.Message}");
        isValid = false;
    }

    var response = new AuthResponse(isValid, isValid ? "ok" : "invalid");
    await TcpHelpers.WriteFrameAsync(ns, response.Write());

    if (!isValid)
    {
        client.Close();
        return;
    }

    await TcpHelpers.WriteFrameAsync(ns, worlds.Write());
    client.Close();
}
