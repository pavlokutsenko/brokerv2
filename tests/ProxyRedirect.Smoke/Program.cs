using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PriceCheck.Collector.Runtime.Driver;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

if (args.Length == 1 && args[0] is "child" or "broker-child")
{
    if (Console.ReadLine() != "go") return;
    using var socket = new TcpClient(AddressFamily.InterNetwork);
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
    await socket.ConnectAsync(IPAddress.Parse("198.51.100.42"), 34455, stop.Token);
    await socket.GetStream().WriteAsync("wfp-smoke"u8.ToArray(), stop.Token);
    if (args[0] == "broker-child")
    {
        var reply = new byte[9];
        await socket.GetStream().ReadExactlyAsync(reply, stop.Token);
        if (!reply.SequenceEqual("wfp-smoke"u8.ToArray())) throw new IOException("Echo mismatch");
    }
    return;
}

using var preflightListener = new TcpListener(IPAddress.Loopback, 0);
preflightListener.Start();
var preflightPort = ((IPEndPoint)preflightListener.LocalEndpoint).Port;
using var preflightTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var preflightServer = Task.Run(async () =>
{
    for (var attempt = 0; attempt < 2; attempt++)
    {
        using var connection = await preflightListener.AcceptTcpClientAsync(preflightTimeout.Token);
        var stream = connection.GetStream();
        var header = new byte[1024];
        var used = 0;
        while (header.AsSpan(0, used).IndexOf("\r\n\r\n"u8) < 0)
        {
            var read = await stream.ReadAsync(header.AsMemory(used), preflightTimeout.Token);
            if (read == 0 || (used += read) == header.Length) throw new IOException("Preflight request missing");
        }
        var request = System.Text.Encoding.ASCII.GetString(header, 0, used);
        var authorized = request.Contains("Proxy-Authorization: Basic dXNlcjpwYXNz\r\n", StringComparison.Ordinal);
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(authorized
            ? "HTTP/1.1 200 Connection Established\r\n\r\n"
            : "HTTP/1.1 407 Proxy Authentication Required\r\n\r\n"), preflightTimeout.Token);
    }
});
var preflightTemplate = new LaunchTemplate
{
    ProxyEnabled = true, ProxyHost = "127.0.0.1", ProxyPort = preflightPort,
    ProxyUser = "wrong", ProxyPassword = "pass"
};
try
{
    await ProxyTcpBroker.VerifyUpstreamAsync(preflightTemplate, preflightTimeout.Token);
    throw new IOException("Invalid proxy credentials unexpectedly passed preflight");
}
catch (InvalidOperationException error) when (error.Message.Contains("407", StringComparison.Ordinal)) { }
preflightTemplate.ProxyUser = "user";
await ProxyTcpBroker.VerifyUpstreamAsync(preflightTemplate, preflightTimeout.Token);
await preflightServer;
Console.WriteLine("PROXY_PREFLIGHT_OK rejects HTTP 407 before launch and accepts HTTP 200");

using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var listenPort = ((IPEndPoint)listener.LocalEndpoint).Port;
using var child = Process.Start(new ProcessStartInfo
{
    FileName = Environment.ProcessPath!, UseShellExecute = false, RedirectStandardInput = true,
    ArgumentList = { "child" }
}) ?? throw new IOException("Child did not start");
using var device = new Lu4Device();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
try
{
    device.SetProxyRedirect(child.Id, listenPort, true);
    await child.StandardInput.WriteLineAsync("go");
    using var client = await listener.AcceptTcpClientAsync(timeout.Token);
    var context = new byte[16];
    var contextSize = client.Client.IOControl(unchecked((int)0x980000DD), null, context);
    var expectedAddress = IPAddress.Parse("198.51.100.42").GetAddressBytes();
    if (contextSize != 16 || BinaryPrimitives.ReadUInt32LittleEndian(context) != 0x5052344C ||
        BinaryPrimitives.ReadInt32LittleEndian(context.AsSpan(4)) != child.Id ||
        !context.AsSpan(8, 4).SequenceEqual(expectedAddress) ||
        BinaryPrimitives.ReadUInt16BigEndian(context.AsSpan(12)) != 34455)
        throw new IOException($"Unexpected WFP context: {Convert.ToHexString(context)}");
    var data = new byte[9];
    await client.GetStream().ReadExactlyAsync(data, timeout.Token);
    if (!data.SequenceEqual("wfp-smoke"u8.ToArray())) throw new IOException("TCP payload mismatch");
    await child.WaitForExitAsync(timeout.Token);
    if (child.ExitCode != 0) throw new IOException($"Child exited {child.ExitCode}");
    Console.WriteLine("WFP_REDIRECT_OK original=198.51.100.42:34455 payload=wfp-smoke");
}
finally
{
    try { device.SetProxyRedirect(child.Id, 0, false); } catch { }
    if (!child.HasExited) child.Kill();
}

using var proxyListener = new TcpListener(IPAddress.Loopback, 0);
proxyListener.Start();
var proxyPort = ((IPEndPoint)proxyListener.LocalEndpoint).Port;
var template = new LaunchTemplate
{
    ProxyEnabled = true, ProxyHost = "127.0.0.1", ProxyPort = proxyPort,
    ProxyUser = "user", ProxyPassword = "pass"
};
using var broker = new ProxyTcpBroker(template);
using var brokerChild = Process.Start(new ProcessStartInfo
{
    FileName = Environment.ProcessPath!, UseShellExecute = false, RedirectStandardInput = true,
    ArgumentList = { "broker-child" }
}) ?? throw new IOException("Broker child did not start");
using var brokerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
try
{
    broker.Bind(brokerChild.Id);
    await brokerChild.StandardInput.WriteLineAsync("go");
    using var proxyClient = await proxyListener.AcceptTcpClientAsync(brokerTimeout.Token);
    var proxyStream = proxyClient.GetStream();
    var request = new byte[8192];
    var used = 0;
    while (request.AsSpan(0, used).IndexOf("\r\n\r\n"u8) < 0)
    {
        var read = await proxyStream.ReadAsync(request.AsMemory(used), brokerTimeout.Token);
        if (read == 0 || (used += read) == request.Length) throw new IOException("CONNECT header missing");
    }
    var header = System.Text.Encoding.ASCII.GetString(request, 0, used);
    if (!header.StartsWith("CONNECT 198.51.100.42:34455 HTTP/1.1\r\n", StringComparison.Ordinal) ||
        !header.Contains("Proxy-Authorization: Basic dXNlcjpwYXNz\r\n", StringComparison.Ordinal))
        throw new IOException($"Unexpected CONNECT header: {header}");
    await proxyStream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), brokerTimeout.Token);
    var payload = new byte[9];
    await proxyStream.ReadExactlyAsync(payload, brokerTimeout.Token);
    if (!payload.SequenceEqual("wfp-smoke"u8.ToArray())) throw new IOException("Proxy payload mismatch");
    await proxyStream.WriteAsync(payload, brokerTimeout.Token);
    await brokerChild.WaitForExitAsync(brokerTimeout.Token);
    if (brokerChild.ExitCode != 0) throw new IOException($"Broker child exited {brokerChild.ExitCode}");
    Console.WriteLine("WFP_BROKER_OK authenticated CONNECT and bidirectional payload");
}
finally
{
    if (!brokerChild.HasExited) brokerChild.Kill();
}
