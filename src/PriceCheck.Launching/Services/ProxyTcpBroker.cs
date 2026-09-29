using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Collector.Services;

// WFP redirects only the selected client's outgoing TCP connects here. The
// client keeps its own Winsock receive path, including its receive transform.
internal sealed partial class ProxyTcpBroker : IDisposable
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
    private readonly bool _tracePorts = Environment.GetEnvironmentVariable("PRICECHECK_TRACE_PORTS") == "1";
    private readonly bool _captureWorldPrefix =
        Environment.GetEnvironmentVariable("PRICECHECK_TEST_CAPTURE_WORLD_PREFIX") == "1";
    private readonly object _traceGate = new();
    private int _pid;
    private bool _bound;

    public ProxyTcpBroker(LaunchTemplate template, bool guarded = false)
    {
        if (template.ProxyEnabled) ClientLaunchConfiguration.ValidateProxy(template);
        _guarded = guarded;
        ProxyEnabled = template.ProxyEnabled;
        _host = template.ProxyEnabled ? template.ProxyHost.Trim() : "";
        _port = template.ProxyEnabled ? template.ProxyPort : 0;
        _authorization = template.ProxyEnabled ? Convert.ToBase64String(Encoding.UTF8.GetBytes(
            template.ProxyUser + ":" + template.ProxyPassword)) : "";
        _listener.Start();
        ListenerPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptAsync);
    }

    public int ListenerPort { get; }

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
        _boundPids.TryAdd(pid, 0);
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
            var worldSession = false;
            try
            {
                var context = new byte[16];
                if (client.Client.IOControl(QueryContext, null, context) != context.Length ||
                    BinaryPrimitives.ReadUInt32LittleEndian(context) != ContextMagic ||
                    !OwnsRedirect(BinaryPrimitives.ReadInt32LittleEndian(context.AsSpan(4))))
                    return;
                Trace("accepted", 1);

                var address = new IPAddress(context.AsSpan(8, 4));
                var port = BinaryPrimitives.ReadUInt16BigEndian(context.AsSpan(12, 2));
                if (port == 0) return;
                Trace("destination_port", port);
                var destination = $"{address}:{port}";
                Trace("destination_ipv4", BinaryPrimitives.ReadInt32BigEndian(context.AsSpan(8, 4)));
                if (_guarded && (port == 2108 || IsWorldPort(port)))
                {
                    while (!AllowLogin) await Task.Delay(100, session.Token);
                }

                var records = new byte[4096];
                var recordsLength = client.Client.IOControl(QueryRecords, null, records);
                if (recordsLength <= 0) return;
                Trace("redirect_records", recordsLength);
                if (_guarded && ProxyEnabled && port == 53)
                {
                    await ServeDnsAsync(client.GetStream(), records.AsSpan(0, recordsLength).ToArray(), session.Token);
                    return;
                }

                using var upstream = new TcpClient(AddressFamily.InterNetwork);
                upstream.Client.IOControl(SetRecords, records.AsSpan(0, recordsLength).ToArray(), null);
                await upstream.ConnectAsync(ProxyEnabled ? _host : address.ToString(), ProxyEnabled ? _port : port, session.Token).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(LaunchTimeouts.NetworkSeconds), session.Token);
                Trace(ProxyEnabled ? "proxy_tcp" : "direct_tcp", ProxyEnabled ? _port : port);
                var network = upstream.GetStream();
                var extra = ProxyEnabled ? await OpenConnectAsync(network, destination, session.Token) : [];
                if (ProxyEnabled) Interlocked.Increment(ref _connections);
                if (IsWorldPort(port)) {
                    Interlocked.Increment(ref _worldConnections);
                    Interlocked.Increment(ref _worldActive); worldSession = true;
                    Interlocked.Exchange(ref _worldOpenedAt, Environment.TickCount64);
                    Interlocked.Exchange(ref _worldSent, 0); Interlocked.Exchange(ref _worldReceived, 0);
                }
                Trace(ProxyEnabled ? "proxy_http_200" : "direct_connected", port);

                var game = client.GetStream();
                Trace("game_stream_ready", port);
                if (extra.Length > 0)
                {
                    await game.WriteAsync(extra, session.Token);
                    CountTraffic("proxy_to_client", port, extra.Length);
                }
                var outgoing = PumpAsync(game, network, "client_to_proxy", port, session.Token);
                var incoming = PumpAsync(network, game, "proxy_to_client", port, session.Token);
                await Task.WhenAny(outgoing, incoming);
                if (!_stop.IsCancellationRequested && (outgoing.IsFaulted || incoming.IsFaulted))
                SetError(ProxyEnabled ? "Game traffic through proxy was interrupted by a network error." : "Game traffic was interrupted by a network error.");
                Trace("tunnel_ended", port);
                session.Cancel();
                upstream.Dispose();
                try { await Task.WhenAll(outgoing, incoming); }
                catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
            catch (Exception error) {
                if (!_stop.IsCancellationRequested && (error is not OperationCanceledException || !session.IsCancellationRequested))
                SetError(error is IOException ? error.Message : ProxyEnabled ? "Proxy connection failed verification or was interrupted." : "Direct connection to the game server was interrupted.");
                Trace("tunnel_error", error.HResult);
            }
            finally { if (worldSession && Interlocked.Decrement(ref _worldActive) == 0) Interlocked.Exchange(ref _worldOpenedAt, 0); }
        }
    }

    private static bool IsConnectSuccess(ReadOnlySpan<byte> response) =>
        response.StartsWith("HTTP/1.1 200 "u8) || response.StartsWith("HTTP/1.0 200 "u8);

    public void Dispose()
    {
        if (_bound)
        {
            try
            {
                using var device = new Lu4Device();
                foreach (var pid in _boundPids.Keys) device.SetProxyRedirect(pid, ListenerPort, false);
            }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception) { }
        }
        _stop.Cancel();
        _listener.Stop();
        try { _acceptLoop.Wait(TimeSpan.FromSeconds(6)); }
        catch (AggregateException) { }
        _stop.Dispose();
    }
}
