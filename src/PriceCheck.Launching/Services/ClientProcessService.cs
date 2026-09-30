using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Contracts;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

namespace PriceCheck.Collector.Services;

public sealed partial class ClientProcessService : IClientProcessService
{
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly ConcurrentDictionary<int, byte> _claimed = new();
    private readonly ConcurrentDictionary<int, ProxyTcpBroker> _proxyBrokers = new();
    private readonly ConcurrentDictionary<int, (int Pid, DateTime Started)> _earlyRoots = new();
    private readonly ConcurrentDictionary<int, ClientLaunchGuard> _guards = new();
    private readonly ConcurrentDictionary<int, PriceCheck.Contracts.ClientProtectionStatus> _lastProtection = new();
    private readonly ConcurrentDictionary<int, string> _pendingLateAgents = new();
    private readonly ConcurrentDictionary<int, ClientResourceJob> _resourceJobs = new();

    public IReadOnlyList<HardwareScanRow> LastHardwareScan { get; private set; } = [];

    public async Task<int> LaunchAndBindAsync(CollectorProfile profile, LaunchTemplate? template, CancellationToken cancellationToken)
    {
        await _launchGate.WaitAsync(cancellationToken);
        ProxyTcpBroker? broker = null;
        ClientLaunchGuard? guard = null;
        LaunchGuardMapping? mapping = null;
        SuspendedClientProcess? suspended = null;
        ClientResourceJob? resourceJob = null;
        var accepted = false;
        int candidate = 0;
        DateTime candidateStarted = DateTime.MinValue;
        try
        {
            if (template is not { HardwareEnabled: true })
                throw new LaunchProtectionException("Launch blocked: select a template with HWID enabled.");
            ClientResourceBudget.Validate(template);
            using (var device = new PriceCheck.Collector.Runtime.Driver.Lu4Device())
                if (device.QueryProxyGuard().Capabilities != 31)
                    throw new LaunchProtectionException("The driver does not support mandatory direct-traffic protection.");
            LastHardwareScan = await Task.Run(HardwareInventoryService.Scan, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            await ProxyTcpBroker.VerifyUpstreamAsync(template, cancellationToken);
            var before = CurrentClientPids().ToHashSet();
            var executable = ResolveLaunchFile(profile);
            var start = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false };
            var agentPath = ClientLaunchConfiguration.Apply(start, template, profile.Id)
                ?? throw new LaunchProtectionException("HWID agent is not ready.");
            mapping = new(start.Environment["PRICECHECK_WORLD_IDENTITY"]!, template.ProxyEnabled);
            start.Environment["PRICECHECK_LAUNCH_GUARD"] = mapping.Name;
            // The experimental early loader is excluded from the mandatory guarded path.
            start.Environment.Remove("PRICECHECK_EARLY_CHILD_INJECT");
            broker = new ProxyTcpBroker(template, guarded: true);
            suspended = new(start);
            var root = suspended.Process;
            if (template.MemoryBudgetEnabled || template.CpuBudgetEnabled)
                resourceJob = ClientResourceJob.Assign(suspended,
                    template.MemoryBudgetEnabled ? template.MemoryBudgetMiB : null,
                    template.CpuBudgetEnabled ? template.CpuBudgetPercent : null);
            broker.Bind(root.Id);
            DriverIdentityService.Bind(root, profile.Id, template.Identity);
            var rootIdentity = (root.Id, root.StartTime.ToUniversalTime());
            guard = new(mapping, broker, root.Id, IsAlive, () =>
            {
                KillCurrent(rootIdentity.Id, rootIdentity.Item2);
                if (candidate != 0) KillCurrent(candidate, candidateStarted);
            });
            suspended.Resume();
            var deadline = DateTime.UtcNow.AddSeconds(LaunchTimeouts.GameStartupSeconds);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (guard.Status.Failed) throw new LaunchProtectionException(guard.Status.Error!);
                using var device = new PriceCheck.Collector.Runtime.Driver.Lu4Device();
                candidate = CurrentClientPids().Where(pid => !before.Contains(pid) && !_claimed.ContainsKey(pid))
                    .OrderByDescending(ProcessStartTime).FirstOrDefault(pid =>
                    {
                        var route = device.QueryProxyGuard(pid);
                        return route.Active && route.HostProcessId == Environment.ProcessId && route.ListenerPort == broker.ListenerPort;
                    });
                if (candidate != 0)
                {
                    if (resourceJob is not null && !resourceJob.Contains(candidate))
                        throw new IOException("Ресурсы: игровой клиент не унаследовал лимиты запуска.");
                    candidateStarted = ProcessStartTime(candidate);
                    broker.BindAdditional(candidate);
                    guard.Bind(candidate);
                    _claimed.TryAdd(candidate, 0);
                    _earlyRoots[candidate] = (root.Id, root.StartTime.ToUniversalTime());
                    _proxyBrokers[candidate] = broker;
                    _guards[candidate] = guard;
                    _pendingLateAgents[candidate] = agentPath;
                    if (resourceJob is not null)
                    {
                        _resourceJobs[candidate] = resourceJob;
                        resourceJob = null;
                    }
                    profile.LaunchFile = executable; profile.LastProcessId = candidate;
                    accepted = true;
                    return candidate;
                }
                if (root.HasExited && root.ExitCode != 0)
                    throw new ClientStartupException($"The launcher exited with code {root.ExitCode} before creating lu4.bin.");
                await Task.Delay(100, cancellationToken);
            }
            throw new TimeoutException("Protected lu4.bin did not appear within five minutes.");
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException ||
            error is InvalidOperationException and not LaunchProtectionException)
        { throw new LaunchProtectionException("Launch blocked: HWID/proxy driver verification failed. " + error.Message, error); }
        finally
        {
            if (!accepted)
            {
                if (_resourceJobs.TryRemove(candidate, out var failedJob)) failedJob.Dispose();
                guard?.Dispose();
                if (suspended is not null && !suspended.Process.HasExited) suspended.Process.Kill(entireProcessTree: true);
                broker?.Dispose();
                if (guard is null) mapping?.Dispose();
            }
            suspended?.Dispose();
            resourceJob?.Dispose();
            _launchGate.Release();
        }
    }

    public async Task WaitForGameWindowAsync(int pid, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(LaunchTimeouts.GameStartupSeconds);
        var stableLargeWindowSamples = 0;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Protection(pid).Error is { } protectionError) throw new LaunchProtectionException(protectionError);
            if (!IsAlive(pid)) throw new InvalidOperationException("The client exited during Active Anticheat checks.");
            var area = LargestVisibleWindowArea(pid);
            stableLargeWindowSamples = area >= 800L * 600L ? stableLargeWindowSamples + 1 : 0;
            if (stableLargeWindowSamples >= 4)
            {
                // The splash and integrity dialogs are much smaller. Waiting
                // for a stable game-sized window keeps our patch outside the
                // startup file-integrity phase while still preceding login.
                await Task.Delay(2000, cancellationToken);
                return;
            }
            await Task.Delay(500, cancellationToken);
        }
        throw new TimeoutException("The game window did not appear within 5 minutes.");
    }

    private static IEnumerable<int> CurrentClientPids() =>
        Process.GetProcessesByName("lu4")
            .Concat(Process.GetProcessesByName("lu4.bin"))
            .Select(process => process.Id)
            .Distinct();

    private async Task WaitForAgentReadyAsync(int pid, ClientAgentHookLoader.HookLease? hook, CancellationToken cancellationToken)
    {
        // Slow machines may still be installing hooks after the window appears.
        // Initialization can wait; a reported protection fault is always immediate.
        var deadline = Environment.TickCount64 + LaunchTimeouts.AgentReadyMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Protection(pid).Error is { } error) throw new LaunchProtectionException(error);
            var session = PriceCheck.Windows.ClientProcessIdentity.Read(pid)
                ?? throw new InvalidOperationException("The client exited before the HWID agent started.");
            // A named event or stale text log cannot authorize another PID.
            // Require the agent's own bound lease and fresh verified heartbeat.
            if (_guards.TryGetValue(pid, out var guard) && guard.IsAgentReady(session)) return;
            hook?.Pulse();
            await Task.Delay(100, cancellationToken);
        }
            throw new LaunchProtectionException($"HWID: agent PID {pid} did not confirm readiness within 60 seconds.");
    }

    private static DateTime ProcessStartTime(int pid)
    {
        return PriceCheck.Windows.ClientProcessIdentity.Read(pid)?.StartedAtUtc.UtcDateTime ?? DateTime.MinValue;
    }

    private static string ResolveLaunchFile(CollectorProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.LaunchFile) && File.Exists(profile.LaunchFile))
            return profile.LaunchFile;
        if (string.IsNullOrWhiteSpace(profile.ClientFolder) || !Directory.Exists(profile.ClientFolder))
            throw new DirectoryNotFoundException("Select the client folder first.");

        var names = new[] { "lu4.bin", "LU4.exe", "L2.exe", "launcher.exe" };
        var roots = new[]
        {
            profile.ClientFolder,
            Path.Combine(profile.ClientFolder, "system"),
            Path.Combine(profile.ClientFolder, "bin"),
            Path.Combine(profile.ClientFolder, "game")
        };
        foreach (var root in roots.Where(Directory.Exists))
        foreach (var name in names)
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("No lu4.bin or launcher.exe was found in the selected folder.");
    }

}
