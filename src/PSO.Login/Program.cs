using System.Net;
using System.Net.Sockets;
using System.Text;
using PSO.Net;

var bind = Environment.GetEnvironmentVariable("PSO_LOGIN_BIND") ?? "127.0.0.1:12000";
var parts = bind.Split(':'); var ep = new IPEndPoint(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
var listener = new TcpListener(ep); listener.Start();
Console.WriteLine($"[login] listening on {bind}");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () =>
    {
        using var ns = client.GetStream();
        await TcpHelpers.WriteFrameAsync(ns, Encoding.UTF8.GetBytes(TcpHelpers.Banner("LOGIN")));
        var payload = await TcpHelpers.ReadFrameAsync(ns);
        if (payload is { Length: > 0 }) {
            await TcpHelpers.WriteFrameAsync(ns, Encoding.UTF8.GetBytes("ECHO " + Encoding.UTF8.GetString(payload)));
        }
        client.Close();
    });
}
