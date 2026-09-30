using System.Net.Sockets;

namespace PriceCheck.Collector.Services;

internal sealed partial class ProxyTcpBroker
{
    private async Task RelayAsync(Stream game, Stream network, int port,
        CancellationTokenSource session, Action closeUpstream)
    {
        var outgoing = PumpAsync(game, network, "client_to_proxy", port, session.Token);
        var incoming = PumpAsync(network, game, "proxy_to_client", port, session.Token);
        await Task.WhenAny(outgoing, incoming);
        Trace("tunnel_ended", port);
        session.Cancel();
        closeUpstream();
        try { await Task.WhenAll(outgoing, incoming); }
        catch (Exception error) when (error is IOException or SocketException or
            OperationCanceledException or ObjectDisposedException)
        {
            // A socket reset does not revoke the verified driver route. Closing
            // this tunnel lets normal client recovery observe the disconnect.
            foreach (var fault in new[] { outgoing, incoming }.Where(task => task.IsFaulted)
                         .SelectMany(task => task.Exception!.Flatten().InnerExceptions))
                if (!_stop.IsCancellationRequested && fault is IOException or SocketException)
                    RecordNetworkError(port, fault);
        }
    }

    private void RecordNetworkError(int port, Exception error)
    {
        var socket = error as SocketException ?? error.InnerException as SocketException;
        var detail = $"port={port}, type={error.GetType().Name}, hresult={error.HResult}, socket={socket?.SocketErrorCode.ToString() ?? "none"}";
        Volatile.Write(ref _networkError, detail);
        if (_pid == 0) return;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheckCollector", "logs");
        try
        {
            Directory.CreateDirectory(directory);
            lock (_traceGate) File.AppendAllText(Path.Combine(directory, $"proxy-network-{_pid}.log"),
                $"{DateTimeOffset.UtcNow:O} {detail}\n");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
    }
}
