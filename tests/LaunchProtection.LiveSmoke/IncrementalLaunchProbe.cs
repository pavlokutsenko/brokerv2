using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Collector.Runtime.Radar;
using PriceCheck.Windows;

// Comparison only: the user requested restoration of the prior launch sequence
// before adding checks individually. This is not an application launch option.
internal static class IncrementalLaunchProbe
{
    internal static async Task RunAsync(CollectorProfile profile, LaunchTemplate template, bool earlyRoute,
        bool nativeGates, bool proxyProbe, bool managedGuard)
    {
        void Log(string text) => Console.WriteLine($"{DateTimeOffset.UtcNow:O} BASELINE {text}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        var before = ClientPids().ToHashSet();
        var executable = profile.LaunchFile ?? throw new IOException("Saved client path is missing.");
        var start = new ProcessStartInfo(executable) {
            WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false
        };
        var agent = ClientLaunchConfiguration.Apply(start, template, profile.Id)!;
        start.Environment.Remove("PRICECHECK_LAUNCH_GUARD");
        using var mapping = nativeGates ? new LaunchGuardMapping(start.Environment["PRICECHECK_WORLD_IDENTITY"]!) : null;
        if (mapping is not null) start.Environment["PRICECHECK_LAUNCH_GUARD"] = mapping.Name;
        using var broker = new ProxyTcpBroker(template, guarded: earlyRoute) { AllowLogin = !managedGuard };
        await ProxyTcpBroker.VerifyUpstreamAsync(template, token);
        using var suspended = earlyRoute ? new SuspendedClientProcess(start) : null;
        using var root = suspended?.Process ?? Process.Start(start) ?? throw new IOException("Client did not start.");
        if (earlyRoute) broker.Bind(root.Id);
        int pid = 0;
        using var guard = managedGuard ? new ClientLaunchGuard(mapping!, broker, root.Id,
            value => ClientProcessIdentity.Read(value) is not null, () => { Kill(pid); Kill(root.Id); }) : null;
        using var pulseStop = new CancellationTokenSource();
        mapping?.ControllerReady(true);
        var pulse = Task.Run(async () => {
            if (managedGuard) return;
            try { while (!pulseStop.IsCancellationRequested) { mapping?.Heartbeat(); await Task.Delay(500, pulseStop.Token); } }
            catch (OperationCanceledException) { }
        });
        var probes = Task.Run(async () => {
            if (!proxyProbe) return;
            try {
                while (!pulseStop.IsCancellationRequested) {
                    await Task.Delay(10000, pulseStop.Token);
                    Log("UPSTREAM_PROBE begin");
                    await ProxyTcpBroker.VerifyUpstreamAsync(template, pulseStop.Token);
                    Log("UPSTREAM_PROBE passed");
                }
            } catch (OperationCanceledException) when (pulseStop.IsCancellationRequested) { }
        });
        if (earlyRoute) suspended!.Resume();
        await using var radar = new RadarSessionManager();
        try
        {
            Log(managedGuard ? "add managed coordinator" : nativeGates ? "add native controller lease and send/login/character gates" : earlyRoute ?
                "root route before first instruction; native readbacks selected by probe build" :
                "ordinary launch; no new guard or API readback checks");
            while (pid == 0)
            {
                token.ThrowIfCancellationRequested();
                pid = ClientPids().FirstOrDefault(p => !before.Contains(p));
                if (pid == 0) await Task.Delay(100, token);
            }
            if (earlyRoute) broker.BindAdditional(pid); else broker.Bind(pid);
            if (guard is not null) guard.Bind(pid); else mapping?.Bind(pid);
            Log($"proxy bound pid={pid}");
            await new ClientProcessService().WaitForGameWindowAsync(pid, token);
            using (var hook = await ClientAgentHookLoader.InstallAsync(pid, agent, token))
            {
                var readyBy = Environment.TickCount64 + 15000;
                for (;;)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var ready = EventWaitHandle.OpenExisting($@"Local\PriceCheckAgentReady_{pid}");
                        if (ready.WaitOne(0)) break;
                    }
                    catch (WaitHandleCannotBeOpenedException) { }
                    if (Environment.TickCount64 > readyBy) throw new TimeoutException("Agent did not become ready.");
                    hook.Pulse(); await Task.Delay(100, token);
                }
            }
            Log("agent ready; attach reader and log in");
            if (guard is not null) await guard.RequireAsync(false, token);
            await radar.StartAsync(pid, null, false, token);
            await new ClientLoginService().EnterAsync(pid, profile, token);
            Log("login returned");
            if (guard is not null) await guard.RequireAsync(true, token);
            if (mapping is not null)
            {
                var state = mapping.Read();
                Log($"NATIVE hardware={state.Hardware} world={state.World} applications={state.WorldCount} error={state.Error}");
                if (!state.Hardware || !state.World || state.Error != 0) throw new IOException("Native guard did not confirm protection.");
            }
            var positionBy = Environment.TickCount64 + 30000;
            while (Environment.TickCount64 < positionBy)
            {
                var snapshot = radar.Snapshot(pid, null, false);
                if (snapshot is not null && Math.Abs(snapshot.PlayerX) > 100 && Math.Abs(snapshot.PlayerY) > 100)
                {
                    Log($"WORLD pid={pid} x={snapshot.PlayerX:F0} y={snapshot.PlayerY:F0} connections={broker.Connections} sent={broker.SentBytes} received={broker.ReceivedBytes}");
                    await Task.Delay(15000, token);
                    if (ClientProcessIdentity.Read(pid) is null) throw new IOException("Client exited during world observation.");
                    var final = radar.Snapshot(pid, null, false);
                    if (final is null || Math.Abs(final.PlayerX) <= 100 || Math.Abs(final.PlayerY) <= 100)
                        throw new IOException("World position disappeared during observation.");
                    Log("PASS"); return;
                }
                await Task.Delay(300, token);
            }
            throw new TimeoutException("No world position after login.");
        }
        catch (Exception error) {
            if (guard is not null) Log("GUARD " + System.Text.Json.JsonSerializer.Serialize(guard.Status));
            Log(error.ToString()); Environment.ExitCode = 1;
        }
        finally
        {
            pulseStop.Cancel(); await pulse;
            await probes;
            mapping?.ControllerReady(false);
            if (pid != 0)
            {
                await radar.StopAsync(pid);
                try { using var process = Process.GetProcessById(pid); if (!process.HasExited) process.Kill(true); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            if (!root.HasExited) root.Kill(true);
        }
    }
    private static IEnumerable<int> ClientPids() => Process.GetProcessesByName("lu4.bin").Select(p => p.Id);
    private static void Kill(int pid)
    {
        if (pid == 0) return;
        try { using var process = Process.GetProcessById(pid); if (!process.HasExited) process.Kill(true); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
