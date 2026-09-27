using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class GuardProxyFixture : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    public bool Reject { get; set; }
    private int _requests;
    public int Requests => Volatile.Read(ref _requests);
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public GuardProxyFixture() { _listener.Start(); _loop = AcceptAsync(); }
    private async Task AcceptAsync()
    {
        try {
            while (!_stop.IsCancellationRequested) {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = ServeAsync(client);
            }
        } catch (OperationCanceledException) { }
    }
    private async Task ServeAsync(TcpClient client)
    {
        using (client) try
        {
            var stream = client.GetStream(); var header = new List<byte>(); var one = new byte[1];
            while (header.Count < 8192 && await stream.ReadAsync(one, _stop.Token) != 0) {
                header.Add(one[0]);
                if (header.Count >= 4 && header.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray())) break;
            }
            var text = Encoding.ASCII.GetString(header.ToArray());
            if (!text.Contains("Proxy-Authorization: Basic ")) throw new Exception("Missing proxy authentication");
            Interlocked.Increment(ref _requests);
            var world = text.StartsWith("CONNECT 198.51.100.99:7782 ");
            if (world) await Task.Delay(250, _stop.Token); // send() completes before CONNECT 200.
            var response = Encoding.ASCII.GetBytes(Reject ? "HTTP/1.1 407 rejected\r\n\r\n" : "HTTP/1.1 200 OK\r\n\r\n");
            // Exercise application bytes coalesced with the CONNECT header.
            if (world && !Reject) response = [..response, ..new byte[69]];
            await stream.WriteAsync(response, _stop.Token);
            if (!world || Reject) return;
            var buffer = new byte[1024];
            for (;;) {
                var read = await stream.ReadAsync(buffer, _stop.Token);
                if (read == 0) return;
                // The initial coalesced payload is the only incoming world data.
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or SocketException) { }
    }
    public void Dispose() { _stop.Cancel(); _listener.Stop(); _loop.GetAwaiter().GetResult(); _stop.Dispose(); }
}
