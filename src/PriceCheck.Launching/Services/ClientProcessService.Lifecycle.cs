using System.Diagnostics;
using PriceCheck.Contracts;

namespace PriceCheck.Collector.Services;

public sealed partial class ClientProcessService
{
    public bool IsAlive(int pid)
    {
        return PriceCheck.Windows.ClientProcessIdentity.Read(pid) is not null;
    }

    public void Release(int? pid)
    {
        if (pid is not int value) return;
        _claimed.TryRemove(value, out _);
        _pendingLateAgents.TryRemove(value, out _);
        if (_guards.TryRemove(value, out var guard)) { _lastProtection[value] = guard.Status; guard.Dispose(); }
        if (_earlyRoots.TryRemove(value, out var rootIdentity))
        {
            KillCurrent(rootIdentity.Pid, rootIdentity.Started);
        }
        if (_proxyBrokers.TryRemove(value, out var broker)) broker.Dispose();
        if (_lastProtection.Count > 64)
            foreach (var key in _lastProtection.Keys.Where(key => key != value).Take(_lastProtection.Count - 64))
                _lastProtection.TryRemove(key, out _);
    }

    public PriceCheck.Contracts.ClientProtectionStatus Protection(int pid) =>
        _guards.TryGetValue(pid, out var guard) ? guard.Status :
        _lastProtection.GetValueOrDefault(pid, PriceCheck.Contracts.ClientProtectionStatus.Pending);

    public Task ValidateProtectionAsync(int pid, bool requireWorld, CancellationToken token) =>
        _guards.TryGetValue(pid, out var guard) ? guard.RequireAsync(requireWorld, token) :
        throw new LaunchProtectionException("Этот процесс не имеет подтверждённой защиты HWID и прокси.");

    public void Terminate(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { Release(pid); }
    }

    public async Task ActivateLateAgentAsync(int pid, CancellationToken cancellationToken)
    {
        if (!_pendingLateAgents.TryGetValue(pid, out var agentPath)) return;
        using var hook = await ClientAgentHookLoader.InstallAsync(pid, agentPath, cancellationToken);
        await WaitForAgentReadyAsync(pid, hook, cancellationToken);
        _pendingLateAgents.TryRemove(pid, out _);
    }

    private static void KillCurrent(int pid, DateTime started)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited && process.StartTime.ToUniversalTime() == started) process.Kill(true);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
