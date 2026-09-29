using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Collector.Runtime.Driver;
using PriceCheck.Windows;

internal static class ReusedRootChecks
{
    public static async Task RunAsync()
    {
        using var root = Process.Start(GuardMonitorChecks.StartInfo("--child-wait"))!;
        using var game = Process.Start(GuardMonitorChecks.StartInfo("--child-wait"))!;
        using var broker = new ProxyTcpBroker(new LaunchTemplate { ProxyEnabled = false }, true);
        broker.Bind(root.Id);
        using var routeDevice = new Lu4Device();
        routeDevice.SetProxyRedirect(game.Id, broker.ListenerPort, true);
        broker.BindAdditional(game.Id);
        using var mapping = new LaunchGuardMapping("11223344556677889900AABBCCDDEEFF", false);
        using var file = MemoryMappedFile.OpenExisting(mapping.Name);
        using var view = file.CreateViewAccessor();
        var rootSession = ClientProcessIdentity.Read(root.Id)!;
        var rootReused = 0;
        using var pulseStop = new CancellationTokenSource();
        var pulse = Task.Run(async () => {
            try { for (;;) { view.Write(24, 1); view.Write(72, Environment.TickCount64); await Task.Delay(100, pulseStop.Token); } }
            catch (OperationCanceledException) { }
        });
        using var guard = new ClientLaunchGuard(mapping, broker, root.Id, _ => true,
            () => { if (!game.HasExited) game.Kill(); },
            session => session == rootSession ? Volatile.Read(ref rootReused) == 0 : ClientProcessIdentity.IsCurrent(session));
        try
        {
            guard.Bind(game.Id); await guard.RequireAsync(false, CancellationToken.None);
            // Windows reused the launcher's PID, but its original birth no longer exists.
            // A raw PID liveness check still returns true in this deterministic fixture.
            Volatile.Write(ref rootReused, 1);
            using var device = new Lu4Device();
            device.SetProxyRedirect(root.Id, broker.ListenerPort, false);
            await Task.Delay(1300);
            if (guard.Status.Failed || game.HasExited) throw new Exception("Reused launcher PID revoked a protected game");
            if (!device.QueryProxyGuard(game.Id).Active) throw new Exception("Game route was not independently protected");
            device.SetProxyRedirect(game.Id, broker.ListenerPort, false);
            var deadline = Environment.TickCount64 + 3000;
            while (!guard.Status.Failed && Environment.TickCount64 < deadline) await Task.Delay(50);
            if (!guard.Status.Failed) throw new Exception("Current game route loss was ignored after launcher reuse");
            Console.WriteLine("GUARD_ROOT_REUSE_OK reused launcher birth ignored; live game route loss stays fatal");
        }
        finally
        {
            pulseStop.Cancel(); await pulse;
            foreach (var child in new[] { root, game }) {
                if (!child.HasExited) child.Kill();
                await child.WaitForExitAsync();
            }
        }
    }
}
