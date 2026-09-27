using System.Net.Sockets;
using System.Net;
using System.Diagnostics;
using System.Text;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class ProxyBannerProbe
{
    internal static async Task ChildAsync()
    {
        if (Console.ReadLine() != "go") return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var socket = new TcpClient(AddressFamily.InterNetwork);
        await socket.ConnectAsync(IPAddress.Parse("194.180.209.45"), 2108, timeout.Token);
        var buffer = new byte[1024];
        var read = await socket.GetStream().ReadAsync(buffer, timeout.Token);
        if (read < 2) throw new IOException("Login server greeting missing.");
        Console.WriteLine($"WFP_BANNER binaryBytes={read}");
    }

    internal static async Task WfpAsync(LaunchTemplate template)
    {
        Environment.SetEnvironmentVariable("PRICECHECK_TRACE_HARDWARE", "1");
        using var broker = new ProxyTcpBroker(template, guarded: true) { AllowLogin = true };
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, RedirectStandardInput = true,
            ArgumentList = { "--banner-child" }
        }) ?? throw new IOException("Banner child did not start.");
        try
        {
            broker.Bind(child.Id);
            await child.StandardInput.WriteLineAsync("go");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Console.WriteLine($"WFP_BANNER exit={child.ExitCode} received={broker.ReceivedBytes} error={broker.Error}");
            if (child.ExitCode != 0) throw new IOException("WFP banner failed.");
        }
        finally { if (!child.HasExited) child.Kill(); }
    }

    internal static async Task RunAsync(LaunchTemplate template)
    {
        foreach (var endpoint in new[] { "194.180.209.45:2108", "185.29.255.102:2108" })
        {
            try { await ProbeAsync(template, endpoint); }
            catch (OperationCanceledException) { Console.WriteLine($"BANNER endpoint={endpoint} CONNECT_HEADER_TIMEOUT"); }
        }
    }

    private static async Task ProbeAsync(LaunchTemplate template, string endpoint)
    {
            const bool keepAlive = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var socket = new TcpClient(AddressFamily.InterNetwork);
            await socket.ConnectAsync(template.ProxyHost, template.ProxyPort, timeout.Token);
            var stream = socket.GetStream();
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(template.ProxyUser + ":" + template.ProxyPassword));
            var request = $"CONNECT {endpoint} HTTP/1.1\r\nHost: {endpoint}\r\nProxy-Authorization: Basic {auth}\r\n" +
                (keepAlive ? "Proxy-Connection: Keep-Alive\r\n" : "") + "\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);
            var header = new List<byte>();
            var one = new byte[1];
            while (header.Count < 8192)
            {
                if (await stream.ReadAsync(one, timeout.Token) == 0) throw new IOException("Missing CONNECT response.");
                header.Add(one[0]);
                if (header.Count >= 4 && header.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
            }
            var status = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n")[0];
            var buffer = new byte[1024];
            try
            {
                var read = await stream.ReadAsync(buffer, timeout.Token);
                Console.WriteLine($"BANNER endpoint={endpoint} status={status} headerBytes={header.Count} binaryBytes={read}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"BANNER endpoint={endpoint} status={status} headerBytes={header.Count} TIMEOUT");
            }
    }
}
