using System.Net.Sockets;
using System.Text;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;
internal sealed partial class ProxyTcpBroker
{
    public static async Task VerifyUpstreamAsync(LaunchTemplate template, CancellationToken cancellationToken)
    {
        if (!template.ProxyEnabled) return;
        ClientLaunchConfiguration.ValidateProxy(template);
        using var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync(template.ProxyHost.Trim(), template.ProxyPort, cancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(LaunchTimeouts.NetworkSeconds), cancellationToken);
            var network = client.GetStream();
            var authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                template.ProxyUser + ":" + template.ProxyPassword));
            var request = Encoding.ASCII.GetBytes($"CONNECT {LoginProbeDestination} HTTP/1.1\r\n" +
                $"Host: {LoginProbeDestination}\r\nProxy-Authorization: Basic {authorization}\r\n\r\n");
            await network.WriteAsync(request, cancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(LaunchTimeouts.NetworkSeconds), cancellationToken);
            var response = new byte[512];
            var used = 0;
            while (used < response.Length && response.AsSpan(0, used).IndexOf("\r\n"u8) < 0)
            {
                var read = await network.ReadAsync(response.AsMemory(used), cancellationToken)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(LaunchTimeouts.NetworkSeconds), cancellationToken);
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
}
