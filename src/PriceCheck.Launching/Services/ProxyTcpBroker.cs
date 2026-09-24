using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Collector.Services;

// WFP redirects only the selected client's outgoing TCP connects here. The
// client keeps its own Winsock receive path, including its receive transform.
internal sealed class ProxyTcpBroker : IDisposable
{
    private const string LoginProbeDestination = "194.180.209.45:2108";
    private const int QueryRecords = unchecked((int)0x980000DC);
    private const int QueryContext = unchecked((int)0x980000DD);
    private const int SetRecords = unchecked((int)0x980000DE);
    private const uint ContextMagic = 0x5052344C;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _host;
    private readonly int _port;
    private readonly string _authorization;
    private readonly Task _acceptLoop;
    private readonly bool _trace = Environment.GetEnvironmentVariable("PRICECHECK_TRACE_HARDWARE") == "1";
    private readonly bool _captureWorldPrefix =
        Environment.GetEnvironmentVariable("PRICECHECK_TEST_CAPTURE_WORLD_PREFIX") == "1";
    private readonly object _traceGate = new();
    private int _pid;
    private bool _bound;

    public ProxyTcpBroker(LaunchTemplate template)
    {
        ClientLaunchConfiguration.ValidateProxy(template);
        _host = template.ProxyHost.Trim();
        _port = template.ProxyPort;
        _authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            template.ProxyUser + ":" + template.ProxyPassword));
        _listener.Start();
        ListenerPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptAsync);
    }

    public int ListenerPort { get; }

    public static async Task VerifyUpstreamAsync(LaunchTemplate template, CancellationToken cancellationToken)
    {
        ClientLaunchConfiguration.ValidateProxy(template);
        using var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync(template.ProxyHost.Trim(), template.ProxyPort, cancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            var network = client.GetStream();
            var authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                template.ProxyUser + ":" + template.ProxyPassword));
            var request = Encoding.ASCII.GetBytes($"CONNECT {LoginProbeDestination} HTTP/1.1\r\n" +
                $"Host: {LoginProbeDestination}\r\nProxy-Authorization: Basic {authorization}\r\n\r\n");
            await network.WriteAsync(request, cancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            var response = new byte[512];
            var used = 0;
            while (used < response.Length && response.AsSpan(0, used).IndexOf("\r\n"u8) < 0)
            {
                var read = await network.ReadAsync(response.AsMemory(used), cancellationToken)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                if (read == 0) break;
                used += read;
            }
            var lineEnd = response.AsSpan(0, used).IndexOf("\r\n"u8);
            var statusLine = lineEnd >= 0 ? Encoding.ASCII.GetString(response, 0, lineEnd) : "";
            if (statusLine.StartsWith("HTTP/1.1 200 ", StringComparison.Ordinal) ||
                statusLine.StartsWith("HTTP/1.0 200 ", StringComparison.Ordinal)) return;
            if (statusLine.StartsWith("HTTP/1.1 407 ", StringComparison.Ordinal) ||
                statusLine.StartsWith("HTTP/1.0 407 ", StringComparison.Ordinal))
                throw new InvalidOperationException("HTTP proxy rejected the username or password (407). Check this launch template.");
            throw new InvalidOperationException($"HTTP proxy could not reach the LU4 login server: {statusLine.Trim()}.");
        }
        catch (Exception error) when (error is SocketException or TimeoutException or IOException)
        {
            throw new InvalidOperationException("Could not connect through the HTTP proxy. Check its host, port, and availability.", error);
        }
    }

    public void Bind(int pid)
    {
        if (_bound) throw new InvalidOperationException("A proxy route is already bound to this process.");
        Volatile.Write(ref _pid, pid);
        try
        {
            using var device = new Lu4Device();
            device.SetProxyRedirect(pid, ListenerPort, true);
        }
        catch
        {
            Volatile.Write(ref _pid, 0);
            throw;
        }
        _bound = true;
        Trace("bound", ListenerPort);
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => ServeAsync(client));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (SocketException) when (_stop.IsCancellationRequested) { break; }
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var session = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            try
            {
                var context = new byte[16];
                if (client.Client.IOControl(QueryContext, null, context) != context.Length ||
                    BinaryPrimitives.ReadUInt32LittleEndian(context) != ContextMagic ||
                    BinaryPrimitives.ReadInt32LittleEndian(context.AsSpan(4)) != Volatile.Read(ref _pid))
                    return;
                Trace("accepted", 1);

                var address = new IPAddress(context.AsSpan(8, 4));
                var port = BinaryPrimitives.ReadUInt16BigEndian(context.AsSpan(12, 2));
                if (port == 0) return;
                Trace("destination_port", port);
                var destination = $"{address}:{port}";

                var records = new byte[4096];
                var recordsLength = client.Client.IOControl(QueryRecords, null, records);
                if (recordsLength <= 0) return;
                Trace("redirect_records", recordsLength);

                using var upstream = new TcpClient(AddressFamily.InterNetwork);
                upstream.Client.IOControl(SetRecords, records.AsSpan(0, recordsLength).ToArray(), null);
                await upstream.ConnectAsync(_host, _port, session.Token).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10), session.Token);
                Trace("proxy_tcp", _port);
                var network = upstream.GetStream();
                var request = Encoding.ASCII.GetBytes($"CONNECT {destination} HTTP/1.1\r\n" +
                    $"Host: {destination}\r\nProxy-Authorization: Basic {_authorization}\r\n" +
                    "Proxy-Connection: Keep-Alive\r\n\r\n");
                await network.WriteAsync(request, session.Token);

                var response = new byte[8192];
                var used = 0;
                var end = -1;
                while (used < response.Length && end < 0)
                {
                    var read = await network.ReadAsync(response.AsMemory(used), session.Token)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(10), session.Token);
                    if (read == 0) return;
                    used += read;
                    end = response.AsSpan(0, used).IndexOf("\r\n\r\n"u8);
                }
                if (end < 0 || !IsConnectSuccess(response.AsSpan(0, used))) return;
                Trace("proxy_http_200", port);

                var game = client.GetStream();
                if (used > end + 4)
                    await game.WriteAsync(response.AsMemory(end + 4, used - end - 4), session.Token);
                var outgoing = PumpAsync(game, network, "client_to_proxy", port, session.Token);
                var incoming = PumpAsync(network, game, "proxy_to_client", port, session.Token);
                await Task.WhenAny(outgoing, incoming);
                Trace("tunnel_ended", port);
                session.Cancel();
                upstream.Dispose();
                try { await Task.WhenAll(outgoing, incoming); }
                catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or
                TimeoutException or ObjectDisposedException) { Trace("tunnel_error", error.HResult); }
        }
    }

    private static bool IsConnectSuccess(ReadOnlySpan<byte> response) =>
        response.StartsWith("HTTP/1.1 200 "u8) || response.StartsWith("HTTP/1.0 200 "u8);

    private async Task PumpAsync(Stream source, Stream destination, string category, int port,
        CancellationToken token)
    {
        var buffer = new byte[8192];
        var prefix = _captureWorldPrefix && port == 7782
            ? new byte[category == "client_to_proxy" ? 84 : 13] : null;
        var prefixLength = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, token);
            if (read == 0)
            {
                Trace(category + "_eof", 0);
                return;
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            Trace(category, read);
            if (prefix is null || prefixLength == prefix.Length) continue;
            var toCopy = Math.Min(read, prefix.Length - prefixLength);
            buffer.AsSpan(0, toCopy).CopyTo(prefix.AsSpan(prefixLength));
            prefixLength += toCopy;
            if (prefixLength == prefix.Length) TraceWorldPrefix(category, prefix);
        }
    }

    private void TraceWorldPrefix(string category, byte[] prefix)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheckCollector", "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"world-prefix-{_pid}.csv");
        lock (_traceGate)
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O},{category},{Convert.ToHexString(prefix)}\n");
    }

    private void Trace(string category, int detail)
    {
        if (!_trace || _pid == 0) return;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheckCollector", "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"proxy-tcp-{_pid}.csv");
        lock (_traceGate) File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O},{category},{detail}\n");
    }

    public void Dispose()
    {
        if (_bound)
        {
            try
            {
                using var device = new Lu4Device();
                device.SetProxyRedirect(_pid, 0, false);
            }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception) { }
        }
        _stop.Cancel();
        _listener.Stop();
        try { _acceptLoop.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        _stop.Dispose();
    }
}
