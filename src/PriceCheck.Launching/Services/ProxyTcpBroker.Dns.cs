using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace PriceCheck.Collector.Services;

internal sealed partial class ProxyTcpBroker
{
    // HTTP proxies commonly deny CONNECT :53. Convert redirected TCP DNS frames
    // to RFC 8484 POST requests through the same authenticated proxy. The pinned
    // resolver address avoids resolving the resolver through the local network.
    private async Task ServeDnsAsync(Stream game, byte[] records, CancellationToken token)
    {
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false,
            ConnectCallback = (_, cancellation) => OpenDnsTunnelAsync(records, cancellation)
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(LaunchTimeouts.DnsSeconds), MaxResponseContentBufferSize = ushort.MaxValue };
        await RelayDnsAsync(game, async (query, cancellation) =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://cloudflare-dns.com/dns-query");
            request.Content = new ByteArrayContent(query);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
            using var response = await http.SendAsync(request, cancellation);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType != "application/dns-message")
                throw new IOException("Proxy DNS: invalid HTTPS response type.");
            var answer = await response.Content.ReadAsByteArrayAsync(cancellation);
            CountTraffic("client_to_proxy", 53, query.Length + 2);
            CountTraffic("proxy_to_client", 53, answer.Length + 2);
            Trace("dns_https_response", answer.Length);
            return answer;
        }, token);
    }

    private async ValueTask<Stream> OpenDnsTunnelAsync(byte[] records, CancellationToken token)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        NetworkStream? stream = null;
        try
        {
            socket.IOControl(SetRecords, records, null);
            await socket.ConnectAsync(_host, _port, token).AsTask().WaitAsync(TimeSpan.FromSeconds(LaunchTimeouts.NetworkSeconds), token);
            stream = new NetworkStream(socket, ownsSocket: true);
            var request = Encoding.ASCII.GetBytes("CONNECT 1.1.1.1:443 HTTP/1.1\r\nHost: 1.1.1.1:443\r\n" +
                $"Proxy-Authorization: Basic {_authorization}\r\n\r\n");
            await stream.WriteAsync(request, token);
            var header = new byte[8192];
            var used = 0;
            // Do not consume any bytes that belong to TLS after the CONNECT header.
            do
            {
                if (used == header.Length) throw new IOException("Proxy DNS: CONNECT response is too large.");
                await stream.ReadExactlyAsync(header.AsMemory(used++, 1), token).AsTask().WaitAsync(TimeSpan.FromSeconds(LaunchTimeouts.NetworkSeconds), token);
            } while (used < 4 || !header.AsSpan(used - 4, 4).SequenceEqual("\r\n\r\n"u8));
            if (!IsConnectSuccess(header.AsSpan(0, used))) throw new IOException("Proxy DNS: CONNECT to the HTTPS resolver was rejected.");
            Interlocked.Increment(ref _connections);
            Trace("dns_proxy_http_200", 443);
            return stream;
        }
        catch
        {
            if (stream is not null) stream.Dispose(); else socket.Dispose();
            throw;
        }
    }

    internal static async Task RelayDnsAsync(Stream game,
        Func<byte[], CancellationToken, Task<byte[]>> resolve, CancellationToken token)
    {
        var prefix = new byte[2];
        while (await game.ReadAsync(prefix.AsMemory(0, 1), token) != 0)
        {
            await game.ReadExactlyAsync(prefix.AsMemory(1, 1), token);
            var length = BinaryPrimitives.ReadUInt16BigEndian(prefix);
            if (length < 12) throw new IOException("Proxy DNS: incomplete request.");
            var query = new byte[length];
            await game.ReadExactlyAsync(query, token);
            var answer = await resolve(query, token);
            if (answer.Length is < 12 or > ushort.MaxValue || answer[0] != query[0] || answer[1] != query[1] || (answer[2] & 0x80) == 0)
                throw new IOException("Proxy DNS: response does not match the request.");
            BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)answer.Length);
            await game.WriteAsync(prefix, token);
            await game.WriteAsync(answer, token);
        }
    }
}
