using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using PriceCheck.Contracts;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class OptionalProxyChecks
{
    private static LaunchTemplate Disabled() => new() { HardwareEnabled = true, ProxyEnabled = false,
        ProxyHost = "invalid host:unused", ProxyPort = -1, ProxyUser = "unused\r\n", ProxyPassword = "unused\r\n" };
    public static async Task ConfigurationAsync()
    {
        var template = Disabled();
        await ProxyTcpBroker.VerifyUpstreamAsync(template, CancellationToken.None);
        using var broker = new ProxyTcpBroker(template, true);
        using var mapping = new LaunchGuardMapping("11223344556677889900AABBCCDDEEFF", false);
        using var map = MemoryMappedFile.OpenExisting(mapping.Name); using var view = map.CreateViewAccessor();
        if (view.ReadInt32(80) != 1 || template.Summary.Contains("запрещён")) throw new Exception("Disabled proxy requires a proxy lease");
        var status = ClientProtectionStatus.Pending with { ProxyRequired = false, Error = "HTTP proxy failed" };
        if (!status.ProxyLabel.Contains("выключен") || status.HardwareLabel.Contains("ОШИБКА"))
            throw new Exception("Proxy setting or error falsely reported HWID failure");
        template.ProxyEnabled = true;
        try { await ProxyTcpBroker.VerifyUpstreamAsync(template, CancellationToken.None); throw new Exception("Invalid enabled proxy accepted"); }
        catch (ArgumentException) { }
        Console.WriteLine("OPTIONAL_PROXY_OK disabled invalid fields ignored, enabled validation retained, independent indicators");
    }
    public static async Task TransportAsync()
    {
        // Loopback destinations are intentionally denied by WFP rather than redirected.
        var address = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
            .First(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254."));
        using var target = new TcpListener(address, 0); target.Start();
        var port = ((IPEndPoint)target.LocalEndpoint).Port;
        using var child = Process.Start(GuardMonitorChecks.StartInfo("--child-direct", port.ToString(), address.ToString()))!;
        using var broker = new ProxyTcpBroker(Disabled(), true); broker.Bind(child.Id);
        using var mapping = new LaunchGuardMapping("11223344556677889900AABBCCDDEEFF", false); mapping.Bind(child.Id);
        using var map = MemoryMappedFile.OpenExisting(mapping.Name); using var view = map.CreateViewAccessor();
        view.Write(24, 1); view.Write(72, Environment.TickCount64);
        using var pulseStop = new CancellationTokenSource();
        var pulse = Task.Run(async () => { try { for (;;) { view.Write(72, Environment.TickCount64); await Task.Delay(100, pulseStop.Token); } }
            catch (OperationCanceledException) { } });
        using var guard = new ClientLaunchGuard(mapping, broker, child.Id, _ => !child.HasExited, () => child.Kill());
        guard.Bind(child.Id);
        try
        {
            await guard.RequireAsync(false, CancellationToken.None);
            await child.StandardInput.WriteLineAsync("go");
            using var accepted = await target.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var bytes = new byte[11]; await accepted.GetStream().ReadExactlyAsync(bytes);
            if (!bytes.SequenceEqual("DIRECT_HWID"u8.ToArray())) throw new Exception("HTTP CONNECT sent with proxy disabled");
            await accepted.GetStream().WriteAsync(bytes);
            if (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) != "DIRECT_OK") throw new Exception("Direct relay failed");
            await Task.Delay(11000);
            await guard.RequireAsync(false, CancellationToken.None);
            if (guard.Status.ProxyRequired || guard.Status.ProxyReady || broker.Connections != 0 || broker.SentBytes != 11 || broker.ReceivedBytes != 11)
                throw new Exception("Disabled proxy ran probes or reported CONNECT protection");
            view.Write(12, 8);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4));
            if (!guard.Status.Failed) throw new Exception("HWID monitor disabled together with proxy");
            Console.WriteLine("DIRECT_RELAY_OK exact TCP echo without CONNECT, no periodic proxy probe, HWID failure still terminates client");
        }
        finally { pulseStop.Cancel(); await pulse; if (!child.HasExited) child.Kill(); }
    }
}
