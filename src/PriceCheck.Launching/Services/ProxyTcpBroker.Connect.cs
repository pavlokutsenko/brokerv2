using System.Text;

namespace PriceCheck.Collector.Services;

internal sealed partial class ProxyTcpBroker
{
    private async Task<byte[]> OpenConnectAsync(Stream network, string destination, CancellationToken token)
    {
        var request = Encoding.ASCII.GetBytes($"CONNECT {destination} HTTP/1.1\r\n" +
            $"Host: {destination}\r\nProxy-Authorization: Basic {_authorization}\r\n" +
            "Proxy-Connection: Keep-Alive\r\n\r\n");
        await network.WriteAsync(request, token);
        var response = new byte[8192];
        var used = 0;
        var end = -1;
        while (used < response.Length && end < 0)
        {
            var read = await network.ReadAsync(response.AsMemory(used), token)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(LaunchTimeouts.NetworkSeconds), token);
            if (read == 0) throw new IOException("Прокси закрыл соединение до CONNECT 200.");
            used += read;
            end = response.AsSpan(0, used).IndexOf("\r\n\r\n"u8);
        }
        if (end < 0 || !IsConnectSuccess(response.AsSpan(0, used)))
        {
            var statusEnd = response.AsSpan(0, used).IndexOf("\r\n"u8);
            var status = statusEnd >= 0 ? Encoding.ASCII.GetString(response, 0, statusEnd) : "неполный ответ";
            throw new IOException($"Прокси отклонил CONNECT к {destination}: {status}.");
        }
        Trace("connect_response_bytes", used);
        Trace("connect_extra_bytes", used - end - 4);
        return response.AsSpan(end + 4, used - end - 4).ToArray();
    }
}
