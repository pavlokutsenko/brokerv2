using System.Text;
namespace PriceCheck.Collector.Services;
internal sealed partial class ProxyTcpBroker
{
    private async Task PumpAsync(Stream source, Stream destination, string category, int port,
        CancellationToken token)
    {
        var buffer = new byte[8192];
        var prefix = _captureWorldPrefix && IsWorldPort(port)
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
            Trace(category + "_read", read);
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            CountTraffic(category, port, read);
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
        if ((!_trace && !(_tracePorts && category == "destination_port")) || _pid == 0) return;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheckCollector", "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"proxy-tcp-{_pid}.csv");
        lock (_traceGate) File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O},{category},{detail}\n");
    }

}
