using System.Net;
using System.Net.Sockets;
using System.Text;

internal static class ProxyServer
{
    public static async Task RunAsync(int port, string logPath)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleAsync(client, logPath));
            }
        }
        finally { listener.Stop(); }
    }

    private static async Task HandleAsync(TcpClient client, string logPath)
    {
        using (client)
        {
            var stream = client.GetStream();
            var header = new List<byte>();
            var buffer = new byte[1024];
            while (!Encoding.ASCII.GetString(header.ToArray()).Contains("\r\n\r\n", StringComparison.Ordinal) && header.Count < 16384)
            {
                var count = await stream.ReadAsync(buffer.AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                if (count <= 0) return;
                header.AddRange(buffer.AsSpan(0, count).ToArray());
            }
            var request = Encoding.ASCII.GetString(header.ToArray());
            var firstLine = request.Split("\r\n", 2)[0];
            if (!request.Contains("Proxy-Authorization: Basic dXNlcjpwYXNz\r\n", StringComparison.Ordinal))
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n"));
                return;
            }
            var parts = firstLine.Split(' ');
            if (parts.Length < 3 || parts[0] != "CONNECT") return;
            await File.AppendAllTextAsync(logPath, $"{DateTimeOffset.UtcNow:O} {parts[1]} authenticated\n");
            var lastColon = parts[1].LastIndexOf(':');
            if (lastColon <= 0 || !int.TryParse(parts[1][(lastColon + 1)..], out var port)) return;
            using var upstream = new TcpClient();
            try { await upstream.ConnectAsync(parts[1][..lastColon], port).WaitAsync(TimeSpan.FromSeconds(10)); }
            catch
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\n\r\n"));
                return;
            }
            if (port == 7782 && upstream.Client.LocalEndPoint is IPEndPoint local)
                await File.AppendAllTextAsync(logPath, $"{DateTimeOffset.UtcNow:O} world local address {local.Address}\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"));
            var remote = upstream.GetStream();
            var fromClient = stream.CopyToAsync(remote);
            var fromRemote = remote.CopyToAsync(stream);
            await Task.WhenAny(fromClient, fromRemote);
        }
    }
}
