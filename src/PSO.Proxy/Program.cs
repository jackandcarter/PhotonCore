using System.Net;
using System.Net.Sockets;

var listen = Environment.GetEnvironmentVariable("PSO_PROXY_LISTEN") ?? "127.0.0.1:13000";
var upstream = Environment.GetEnvironmentVariable("PSO_PROXY_UPSTREAM") ?? "127.0.0.1:12000";
var lep = new IPEndPoint(IPAddress.Parse(listen.Split(':')[0]), int.Parse(listen.Split(':')[1]));
var uep = new IPEndPoint(IPAddress.Parse(upstream.Split(':')[0]), int.Parse(upstream.Split(':')[1]));
var listener = new TcpListener(lep); listener.Start();
Console.WriteLine($"[proxy] {listen} -> {upstream}");
while (true) {
    var inbound = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () =>
    {
        using var outbound = new TcpClient(); await outbound.ConnectAsync(uep);
        using var ci = inbound.GetStream(); using var co = outbound.GetStream();
        var t1 = ci.CopyToAsync(co); var t2 = co.CopyToAsync(ci); await Task.WhenAny(t1, t2);
    });
}
