using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

// Owns the external proxy socket. LU4 exchanges bytes through a per-launch
// named pipe so the protected process never connects to a disallowed address.
internal sealed class ProxyPipeBroker : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _authorization;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;
    private readonly bool _trace = Environment.GetEnvironmentVariable("PRICECHECK_TRACE_HARDWARE") == "1";
    private readonly object _traceGate = new();
    private int _expectedPid;

    public string PipeName { get; } = "PriceCheckProxy_" + Guid.NewGuid().ToString("N");

    public ProxyPipeBroker(LaunchTemplate template)
    {
        ClientLaunchConfiguration.ValidateProxy(template);
        _host = template.ProxyHost.Trim();
        _port = template.ProxyPort;
        _authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(template.ProxyUser + ":" + template.ProxyPassword));
        _acceptLoop = Task.Run(AcceptAsync);
    }

    public void Bind(int pid) => Volatile.Write(ref _expectedPid, pid);

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 254,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(_stop.Token);
                var connected = pipe;
                pipe = null;
                _ = Task.Run(() => ServeAsync(connected));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (IOException) when (_stop.IsCancellationRequested) { break; }
            finally { pipe?.Dispose(); }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var session = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            try
            {
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientPid) ||
                    clientPid != (uint)Volatile.Read(ref _expectedPid)) return;
                Trace("pipe_client", 1);
                var header = new byte[10];
                await pipe.ReadExactlyAsync(header, session.Token);
                if (!header.AsSpan(0, 4).SequenceEqual("PCPX"u8)) return;
                var address = new IPAddress(header.AsSpan(4, 4));
                var port = header[8] * 256 + header[9];
                if (port is < 1 or > 65535) return;
                var destination = $"{address}:{port}";

                using var upstream = new TcpClient(AddressFamily.InterNetwork);
                await upstream.ConnectAsync(_host, _port, session.Token).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10), session.Token);
                Trace("proxy_tcp", port);
                if (port == 7782 && upstream.Client.LocalEndPoint is IPEndPoint localEndpoint &&
                    localEndpoint.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    var octets = localEndpoint.Address.GetAddressBytes();
                    Trace("world_local_ipv4", (octets[0] << 24) | (octets[1] << 16) | (octets[2] << 8) | octets[3]);
                }
                var network = upstream.GetStream();
                var request = Encoding.ASCII.GetBytes($"CONNECT {destination} HTTP/1.1\r\nHost: {destination}\r\nProxy-Authorization: Basic {_authorization}\r\nProxy-Connection: Keep-Alive\r\n\r\n");
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
                if (end < 0 || !Encoding.ASCII.GetString(response, 0, used).StartsWith("HTTP/1.1 200 ", StringComparison.Ordinal) &&
                    !Encoding.ASCII.GetString(response, 0, used).StartsWith("HTTP/1.0 200 ", StringComparison.Ordinal))
                {
                    Trace("proxy_http_reject", 1);
                    await pipe.WriteAsync(new byte[] { 0 }, session.Token);
                    return;
                }
                Trace("proxy_http_200", port);
                await pipe.WriteAsync(new byte[] { 1 }, session.Token);
                if (used > end + 4)
                    await pipe.WriteAsync(response.AsMemory(end + 4, used - end - 4), session.Token);

                var outgoing = PumpAsync(pipe, network, "client_to_proxy", session.Token);
                var incoming = PumpAsync(network, pipe, "proxy_to_client", session.Token);
                await Task.WhenAny(outgoing, incoming);
                Trace("tunnel_ended", 1);
                session.Cancel();
                upstream.Dispose();
                try { await Task.WhenAll(outgoing, incoming); }
                catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or
                TimeoutException or ObjectDisposedException) { Trace("tunnel_error", error.HResult); }
        }
    }

    private async Task PumpAsync(Stream source, Stream destination, string category, CancellationToken token)
    {
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, token);
            if (read == 0) return;
            if (category == "proxy_to_client" && read == 13)
                Trace("world_upstream_0", BitConverter.ToInt32(buffer, 0));
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            Trace(category, read);
        }
    }

    private void Trace(string category, int detail)
    {
        if (!_trace) return;
        var pid = Volatile.Read(ref _expectedPid);
        if (pid == 0) return;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheckCollector", "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"proxy-broker-{pid}.csv");
        lock (_traceGate) File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O},{category},{detail}\n");
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _acceptLoop.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        _stop.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientPid);
}
