using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using PriceCheck.Collector.Services;

internal static class AgentReadinessChecks
{
    public static async Task RunAsync()
    {
        using var child = Process.Start(GuardMonitorChecks.StartInfo("--child-wait"))!;
        using var broker = new ProxyTcpBroker(new() { ProxyEnabled = false }, true);
        broker.Bind(child.Id);
        using var mapping = new LaunchGuardMapping("11223344556677889900AABBCCDDEEFF", false);
        mapping.Bind(child.Id);
        using var file = MemoryMappedFile.OpenExisting(mapping.Name);
        using var view = file.CreateViewAccessor();
        view.Write(24, 1); view.Write(72, Environment.TickCount64);
        using var stop = new CancellationTokenSource();
        var pulse = Task.Run(async () =>
        {
            try { for (;;) { view.Write(72, Environment.TickCount64); await Task.Delay(100, stop.Token); } }
            catch (OperationCanceledException) { }
        });
        using var guard = new ClientLaunchGuard(mapping, broker, child.Id, _ => !child.HasExited, () => child.Kill());
        guard.Bind(child.Id);
        var service = new ClientProcessService();
        var guards = (ConcurrentDictionary<int, ClientLaunchGuard>)typeof(ClientProcessService)
            .GetField("_guards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        guards[child.Id] = guard;
        var method = typeof(ClientProcessService).GetMethod("WaitForAgentReadyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task Wait(CancellationToken token) => (Task)method.Invoke(service, [child.Id, null, token])!;
        try
        {
            await guard.RequireAsync(false, CancellationToken.None);
            using (var misleadingEvent = new EventWaitHandle(true, EventResetMode.ManualReset, $@"Local\PriceCheckAgentReady_{child.Id}"))
            using (var timeout = new CancellationTokenSource(600))
            {
                try { await Wait(timeout.Token); throw new Exception("Named event authorized an unfinished agent"); }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            }
            // Reproduce a slow startup beyond the old 15-second deadline.
            using var slowDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(22));
            var slowStart = Wait(slowDeadline.Token);
            await Task.Delay(TimeSpan.FromSeconds(16));
            view.Write(24, 5); // HWID + explicit completed initialization, no named event.
            await slowStart.WaitAsync(TimeSpan.FromSeconds(2));
            var session = PriceCheck.Windows.ClientProcessIdentity.Read(child.Id)!;
            var birth = view.ReadInt64(64);
            view.Write(64, birth + 1);
            if (mapping.IsAgentReady(session)) throw new Exception("Readiness accepted another process generation");
            view.Write(64, birth); view.Write(24, 4);
            if (mapping.IsAgentReady(session)) throw new Exception("Ready flag bypassed HWID validation");
            stop.Cancel(); await pulse;
            view.Write(24, 5); view.Write(72, Environment.TickCount64 - 6000);
            if (!mapping.IsAgentReady(session)) throw new Exception("Agent within the longer heartbeat grace was rejected");
            view.Write(24, 5); view.Write(72, Environment.TickCount64 - PriceCheck.Windows.LaunchTimeouts.HeartbeatMilliseconds - 1000);
            if (mapping.IsAgentReady(session)) throw new Exception("Stale agent authorized readiness");
            Console.WriteLine("AGENT_READY_LEASE_OK fake event denied, 16-second startup accepted without event, wrong birth/HWID/stale heartbeat denied");
        }
        finally
        {
            stop.Cancel(); await pulse;
            if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
        }
    }
}
