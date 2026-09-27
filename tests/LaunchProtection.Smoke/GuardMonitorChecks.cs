using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Net.Sockets;
using System.Net;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Collector.Runtime.Driver;

internal static class GuardMonitorChecks
{
    public static async Task ChildAsync(string[] args)
    {
        if (args[0] == "--child-direct") {
            if (Console.ReadLine() != "go") return;
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(args[2], int.Parse(args[1]));
            await client.GetStream().WriteAsync("DIRECT_HWID"u8.ToArray());
            var echo = new byte[11]; await client.GetStream().ReadExactlyAsync(echo);
            if (!echo.SequenceEqual("DIRECT_HWID"u8.ToArray())) throw new Exception("Direct relay changed the payload");
            Console.WriteLine("DIRECT_OK"); Console.ReadLine(); return;
        }
        if (args[0] == "--child-suspended") {
            await File.WriteAllTextAsync(args[1], "STARTED");
            using var tcp = new TcpClient();
            try { await tcp.ConnectAsync("127.0.0.1", int.Parse(args[2])); }
            catch (SocketException) { await File.WriteAllTextAsync(args[1], "BLOCKED"); }
            await Task.Delay(10000); return;
        }
        if (args[0] == "--child-wait") { Console.ReadLine(); return; }
        using var map = MemoryMappedFile.OpenExisting(args[1]);
        using var view = map.CreateViewAccessor();
        while (Console.ReadLine() is { } command)
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync("198.51.100.99", 7782);
            if (command == "go") {
                var world = new byte[16]; view.ReadArray(48, world, 0, 16);
                view.Write(96, Environment.TickCount64); view.WriteArray(32, world, 0, 16);
                view.Write(88, Environment.TickCount64); view.Write(28, view.ReadInt32(28) + 1); view.Write(24, 3);
            }
            await client.GetStream().WriteAsync(new byte[69]);
            var echo = new byte[69]; await client.GetStream().ReadExactlyAsync(echo);
            Console.WriteLine("WORLD");
            Console.ReadLine(); // Parent closes this world before opening the next.
            Console.WriteLine("CLOSED");
        }
    }
    public static async Task RunAsync()
    {
        using var fixture = new GuardProxyFixture();
        var template = new LaunchTemplate { ProxyEnabled = true, ProxyHost = "127.0.0.1", ProxyPort = fixture.Port,
            ProxyUser = "synthetic", ProxyPassword = "synthetic" };
        using var forbidden = new TcpListener(IPAddress.Loopback, 0); forbidden.Start();
        var forbiddenPort = ((IPEndPoint)forbidden.LocalEndpoint).Port;
        var start = StartInfo("--child-suspended", Path.GetFullPath($"workspace/guard-start-{Guid.NewGuid():N}.txt"), forbiddenPort.ToString());
        using (var process = new SuspendedClientProcess(start)) {
            await Task.Delay(100);
            if (File.Exists(start.ArgumentList[1])) throw new Exception("Suspended process executed before binding");
            using var device = new Lu4Device();
            device.SetProxyRedirect(process.Process.Id, fixture.Port, true);
            process.Resume();
            await UntilAsync(() => StartupBlocked(start.ArgumentList[1]));
            if (device.QueryProxyGuard(process.Process.Id).BlockedConnections == 0) throw new Exception("Startup denial unobserved");
            if (forbidden.Pending()) throw new Exception("Suspended child reached a forbidden direct endpoint");
            process.Process.Kill(); await process.Process.WaitForExitAsync();
        }
        Console.WriteLine("SUSPENDED_START_OK policy installed before first instruction");
        foreach (var failure in new[] { "native", "heartbeat", "route", "proxy", "exit" })
        {
            fixture.Reject = false;
            using var child = Process.Start(StartInfo("--child-wait"))!;
            using var broker = new ProxyTcpBroker(template, true); broker.Bind(child.Id);
            using var mapping = new LaunchGuardMapping("11223344556677889900AABBCCDDEEFF");
            mapping.Bind(child.Id);
            using var viewMap = MemoryMappedFile.OpenExisting(mapping.Name);
            using var view = viewMap.CreateViewAccessor();
            view.Write(24, 1); view.Write(72, Environment.TickCount64);
            using var heartbeat = new CancellationTokenSource();
            var pulse = Task.Run(async () => { try { for (;;) { view.Write(72, Environment.TickCount64); await Task.Delay(100, heartbeat.Token); } } catch (OperationCanceledException) { } });
            using var guard = new ClientLaunchGuard(mapping, broker, child.Id, _ => !child.HasExited, () => child.Kill());
            guard.Bind(child.Id);
            await guard.RequireAsync(false, CancellationToken.None);
            if (failure == "exit") {
                child.Kill(); await child.WaitForExitAsync();
                await UntilAsync(() => !guard.Status.ProxyReady);
                if (guard.Status.Failed) throw new Exception("Natural exit was misreported as protection failure");
                heartbeat.Cancel(); await pulse;
                Console.WriteLine("NATURAL_EXIT_OK normal exit remains eligible for recovery");
                continue;
            }
            if (failure == "native") view.Write(12, 8);
            if (failure == "heartbeat") { heartbeat.Cancel(); await pulse; view.Write(72, Environment.TickCount64 - 6000); }
            if (failure == "route") { using var device = new Lu4Device(); device.SetProxyRedirect(child.Id, broker.ListenerPort, false); }
            if (failure == "proxy") fixture.Reject = true;
            await UntilAsync(() => guard.Status.Failed && child.HasExited, failure == "proxy" ? 15000 : 4000);
            heartbeat.Cancel(); await pulse;
            Console.WriteLine("CONTINUOUS_GUARD_OK " + failure + " failure terminated client");
        }
        fixture.Reject = false;
        await WorldAsync(template);
    }
    private static async Task WorldAsync(LaunchTemplate template)
    {
        using var mapping = new LaunchGuardMapping("11223344556677889900AABBCCDDEEFF");
        using var child = Process.Start(StartInfo("--child-world", mapping.Name))!;
        using var broker = new ProxyTcpBroker(template, true); broker.Bind(child.Id); mapping.Bind(child.Id);
        using var viewMap = MemoryMappedFile.OpenExisting(mapping.Name); using var view = viewMap.CreateViewAccessor();
        view.Write(24, 1); view.Write(72, Environment.TickCount64);
        using var guard = new ClientLaunchGuard(mapping, broker, child.Id, _ => !child.HasExited, () => child.Kill());
        guard.Bind(child.Id); await guard.RequireAsync(false, CancellationToken.None);
        await child.StandardInput.WriteLineAsync("go");
        if (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)) != "WORLD")
            throw new Exception("World tunnel failed: " + await child.StandardError.ReadToEndAsync());
        await guard.RequireAsync(true, CancellationToken.None);
        await child.StandardInput.WriteLineAsync("close");
        if (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)) != "CLOSED") throw new Exception("World close failed");
        await UntilAsync(() => !guard.Status.WorldIdentityApplied);
        await child.StandardInput.WriteLineAsync("stale");
        if (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4)) != "WORLD") throw new Exception("Second world failed");
        await Task.Delay(600);
        if (guard.Status.WorldIdentityApplied) throw new Exception("Prior world authorized the new world");
        view.Write(72, Environment.TickCount64); view.Write(96, Environment.TickCount64);
        view.Write(88, Environment.TickCount64); view.Write(28, 2);
        await guard.RequireAsync(true, CancellationToken.None);
        child.Kill(); await child.WaitForExitAsync();
        Console.WriteLine("WORLD_GENERATION_OK application before CONNECT accepted; prior world cannot authorize reconnect");
    }
    internal static ProcessStartInfo StartInfo(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return start;
    }
    private static async Task UntilAsync(Func<bool> ready, int milliseconds = 4000)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (!ready()) { if (Environment.TickCount64 >= deadline) throw new Exception("Guard observation timed out"); await Task.Delay(50); }
    }
    private static bool StartupBlocked(string path)
    {
        try { return File.ReadAllText(path) == "BLOCKED"; }
        catch (IOException) { return false; } // Child may still hold its status file open.
    }
}
