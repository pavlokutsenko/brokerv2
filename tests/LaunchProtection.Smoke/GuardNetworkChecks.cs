using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PriceCheck.Collector.Runtime.Driver;

internal static class GuardNetworkChecks
{
    public static async Task ChildAsync(string[] args)
    {
        if (args[0] == "--child-controller")
        {
            using var device = new Lu4Device();
            device.SetProxyRedirect(int.Parse(args[1]), int.Parse(args[2]), true);
            Console.WriteLine("READY");
            await Task.Delay(Timeout.Infinite); return;
        }
        if (Console.ReadLine() != "go") return;
        if (args[0] == "--child-udp")
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(args[2]), int.Parse(args[1]));
            using var datagram = new UdpClient(endpoint.AddressFamily);
            try { await datagram.SendAsync("blocked-sendto"u8.ToArray(), endpoint); } catch (SocketException) { }
            using var connected = new UdpClient(endpoint.AddressFamily);
            try { connected.Connect(endpoint); await connected.SendAsync("blocked-connected"u8.ToArray()); } catch (SocketException) { }
            Console.WriteLine("UDP_ATTEMPTED"); Console.ReadLine(); return;
        }
        if (args[0] == "--child-parent")
        {
            using var child = Start("--child-attempt", args[1..]);
            await child.StandardInput.WriteLineAsync("go");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
            if (child.ExitCode != 0) throw new Exception("Inherited child leaked");
            Console.WriteLine("INHERITED " + (await child.StandardOutput.ReadToEndAsync()).Trim().Replace("\r", "").Replace("\n", " | "));
            Console.ReadLine(); return;
        }
        foreach (var (address, port) in new[] { (IPAddress.Loopback, int.Parse(args[1])),
            (IPAddress.IPv6Loopback, int.Parse(args[2])) })
        {
            using var socket = new TcpClient(address.AddressFamily);
            try { await socket.ConnectAsync(address, port).WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (SocketException) { continue; }
            throw new Exception("Direct TCP egress was allowed");
        }
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        try { await udp.SendAsync("no-direct-udp"u8.ToArray(), new IPEndPoint(IPAddress.Loopback, int.Parse(args[3]))); Console.WriteLine("UDP_SEND_COMPLETED"); }
        catch (SocketException e) { Console.WriteLine("UDP_DENIED " + e.SocketErrorCode); }
        try {
            using var raw = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp);
            raw.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            throw new Exception("Raw socket was allowed");
        } catch (SocketException e) { Console.WriteLine("RAW_DENIED " + e.SocketErrorCode); }
        Console.WriteLine("BLOCKED");
    }
    public static async Task RunAsync()
    {
        using var device = new Lu4Device();
        if (device.QueryProxyGuard().Capabilities != 31) throw new Exception("Guard capabilities absent");
        using var v4 = new TcpListener(IPAddress.Loopback, 0); v4.Start();
        using var v6 = new TcpListener(IPAddress.IPv6Loopback, 0); v6.Start();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var udp6 = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        using var relay = new TcpListener(IPAddress.Loopback, 0); relay.Start();
        var relayPort = ((IPEndPoint)relay.LocalEndpoint).Port;
        var ports = new[] { ((IPEndPoint)v4.LocalEndpoint).Port.ToString(),
            ((IPEndPoint)v6.LocalEndpoint).Port.ToString(), ((IPEndPoint)udp.Client.LocalEndPoint!).Port.ToString() };
        using (var baseline = new UdpClient())
        {
            await baseline.SendAsync("baseline"u8.ToArray(), (IPEndPoint)udp.Client.LocalEndPoint!);
            var received = await udp.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
            if (!received.Buffer.SequenceEqual("baseline"u8.ToArray())) throw new Exception("UDP baseline failed");
        }
        using (var baselineRaw = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp))
            baselineRaw.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        foreach (var receiver in new[] { udp, udp6 })
        using (var child = Start("--child-udp", [((IPEndPoint)receiver.Client.LocalEndPoint!).Port.ToString(), ((IPEndPoint)receiver.Client.LocalEndPoint!).Address.ToString()]))
        {
            device.SetProxyRedirect(child.Id, relayPort, true);
            await child.StandardInput.WriteLineAsync("go");
            if (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) != "UDP_ATTEMPTED")
                throw new Exception("UDP probe did not finish");
            await Task.Delay(300);
            var policy = device.QueryProxyGuard(child.Id);
            if (receiver.Available != 0 || policy.BlockedConnections < 2 || policy.BlockedProtocol != 17)
                throw new Exception($"UDP egress denial not confirmed: {policy}");
            await child.StandardInput.WriteLineAsync("quit");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Console.WriteLine("KERNEL_UDP_OK IPv4/IPv6 connected UDP and sendto delivered zero packets; driver recorded denials");

        using (var child = Start("--child-attempt", ports))
        {
            device.SetProxyRedirect(child.Id, relayPort, true);
            await child.StandardInput.WriteLineAsync("go");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
            if (child.ExitCode != 0) throw new Exception(await child.StandardError.ReadToEndAsync());
            var result = await child.StandardOutput.ReadToEndAsync();
            await Task.Delay(300);
            RequireNoDelivery(v4, v6, udp);
            RequireUdpDenied(result);
        }
        Console.WriteLine("KERNEL_DENY_OK direct IPv4 TCP, IPv6 TCP and UDP delivered zero packets; raw ICMP bind denied");
        using (var parent = Start("--child-parent", ports))
        {
            device.SetProxyRedirect(parent.Id, relayPort, true);
            await parent.StandardInput.WriteLineAsync("go");
            var inherited = await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(12));
            if (inherited?.StartsWith("INHERITED ") != true)
                throw new Exception("Child inheritance failed: " + await parent.StandardError.ReadToEndAsync());
            var denied = device.QueryProxyGuard(parent.Id).BlockedConnections;
            RequireNoDelivery(v4, v6, udp);
            RequireUdpDenied(inherited);
            // Winsock may reject IPv6 before AUTH_CONNECT; don't assume one
            // callout invocation per API attempt. Both UDP and TCP must fail.
            if (denied < 2) throw new Exception($"Denied attempts not observed for inherited group: {denied}; {inherited}");
            await parent.StandardInput.WriteLineAsync("quit");
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Console.WriteLine("KERNEL_INHERIT_OK descendants protected before binding; denied attempts retained after child exit");
        using (var child = Start("--child-attempt", ports))
        {
            device.SetProxyRedirect(child.Id, relayPort, true);
            device.SetProxyRedirect(child.Id, relayPort, false);
            if (device.QueryProxyGuard(child.Id).Active) throw new Exception("Revoked guard still active");
            await child.StandardInput.WriteLineAsync("go");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
            if (child.ExitCode != 0) throw new Exception(await child.StandardError.ReadToEndAsync());
            RequireUdpDenied(await child.StandardOutput.ReadToEndAsync());
            RequireNoDelivery(v4, v6, udp);
        }
        Console.WriteLine("KERNEL_REVOKE_OK disabling relay never enables direct traffic");
        using (var child = Start("--child-attempt", ports))
        using (var controller = Start("--child-controller", [child.Id.ToString(), relayPort.ToString()]))
        {
            if (await controller.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) != "READY")
                throw new Exception("Independent controller did not bind");
            controller.Kill(); await controller.WaitForExitAsync();
            await child.StandardInput.WriteLineAsync("go");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
            if (child.ExitCode != 0) throw new Exception(await child.StandardError.ReadToEndAsync());
            RequireUdpDenied(await child.StandardOutput.ReadToEndAsync());
            RequireNoDelivery(v4, v6, udp);
        }
        Console.WriteLine("KERNEL_CONTROLLER_EXIT_OK orphaned client retains network denial");
    }
    private static Process Start(string mode, string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add(mode);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new Exception("Synthetic process did not start");
    }
    private static void RequireNoDelivery(TcpListener v4, TcpListener v6, UdpClient udp)
    {
        if (v4.Pending() || v6.Pending() || udp.Available != 0) throw new Exception("Protected traffic reached a direct endpoint");
    }
    private static void RequireUdpDenied(string result)
    {
        // Windows can report a queued UDP send as successful even when WFP
        // drops its first packet. Check real delivery and driver denial above.
        if (!(result.Contains("UDP_DENIED ") || result.Contains("UDP_SEND_COMPLETED")) || !result.Contains("RAW_DENIED "))
            throw new Exception("UDP attempt/raw denial was not observed: " + result);
    }
}
