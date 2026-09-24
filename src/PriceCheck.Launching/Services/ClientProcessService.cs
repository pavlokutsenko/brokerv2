using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Contracts;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

namespace PriceCheck.Collector.Services;

public sealed class ClientProcessService : IClientProcessService
{
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly HashSet<int> _claimed = [];
    private readonly ConcurrentDictionary<int, ProxyTcpBroker> _proxyBrokers = new();
    private readonly ConcurrentDictionary<int, int> _earlyRoots = new();
    private readonly ConcurrentDictionary<int, string> _pendingLateAgents = new();

    public bool IsAlive(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch { return false; }
    }

    public void Release(int? pid)
    {
        if (pid is not int value) return;
        _claimed.Remove(value);
        _pendingLateAgents.TryRemove(value, out _);
        if (_proxyBrokers.TryRemove(value, out var broker)) broker.Dispose();
        if (_earlyRoots.TryRemove(value, out var rootPid))
        {
            try
            {
                using var root = Process.GetProcessById(rootPid);
                if (!root.HasExited) root.Kill();
            }
            catch (ArgumentException) { }
        }
    }

    public void Terminate(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { }
        finally { Release(pid); }
    }

    public async Task ActivateLateAgentAsync(int pid, CancellationToken cancellationToken)
    {
        if (!_pendingLateAgents.TryGetValue(pid, out var agentPath)) return;
        using var hook = await ClientAgentHookLoader.InstallAsync(pid, agentPath, cancellationToken);
        await WaitForAgentReadyAsync(pid, hook, cancellationToken);
        _pendingLateAgents.TryRemove(pid, out _);
    }

    public async Task<int> LaunchAndBindAsync(CollectorProfile profile, LaunchTemplate? template, CancellationToken cancellationToken)
    {
        await _launchGate.WaitAsync(cancellationToken);
        ProxyTcpBroker? pendingBroker = null;
        try
        {
            var before = CurrentClientPids().ToHashSet();
            var executable = ResolveLaunchFile(profile);
            var start = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = true
            };
            var agentPath = ClientLaunchConfiguration.Apply(start, template, profile.Id);
            if (template?.ProxyEnabled == true)
            {
                await ProxyTcpBroker.VerifyUpstreamAsync(template, cancellationToken);
                pendingBroker = new ProxyTcpBroker(template);
            }
            var selectedEarlyProfile = Environment.GetEnvironmentVariable("PRICECHECK_EARLY_ROOT_ONLY_PROFILE");
            var rootOnly = agentPath is not null &&
                string.Equals(profile.Name, selectedEarlyProfile, StringComparison.OrdinalIgnoreCase);
            var early = agentPath is not null && (rootOnly ||
                Environment.GetEnvironmentVariable("PRICECHECK_EARLY_LAUNCH") == "1");
            if (rootOnly)
            {
                start.Environment["PRICECHECK_EARLY_CHILD_SKIP_AGENT"] = "1";
                start.Environment["PRICECHECK_EARLY_IAT_PASSTHROUGH"] = "1";
                start.Environment["PRICECHECK_EARLY_FIRMWARE_SPOOF"] = "1";
            }
            using var root = early
                ? EarlyClientAgentLoader.Start(start, agentPath!, cancellationToken)
                : Process.Start(start) ?? throw new InvalidOperationException("Windows did not start the client.");

            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = CurrentClientPids()
                    .Where(pid => !before.Contains(pid) && !_claimed.Contains(pid))
                    .OrderByDescending(ProcessStartTime)
                    .FirstOrDefault();
                if (candidate != 0)
                {
                    _claimed.Add(candidate);
                    if (early) _earlyRoots[candidate] = root.Id;
                    if (pendingBroker is not null)
                    {
                        try
                        {
                            pendingBroker.Bind(candidate);
                            _proxyBrokers[candidate] = pendingBroker;
                            pendingBroker = null;
                        }
                        catch
                        {
                            Terminate(candidate);
                            if (root.Id != candidate) Terminate(root.Id);
                            throw;
                        }
                    }
                    if (agentPath is not null)
                    {
                        try
                        {
                            if (rootOnly) _pendingLateAgents[candidate] = agentPath;
                            else if (early) await WaitForAgentReadyAsync(candidate, null, cancellationToken);
                            else
                            {
                                using var hook = await ClientAgentHookLoader.InstallAsync(candidate, agentPath, cancellationToken);
                                await WaitForAgentReadyAsync(candidate, hook, cancellationToken);
                            }
                        }
                        catch
                        {
                            Terminate(candidate);
                            if (root.Id != candidate) Terminate(root.Id);
                            throw;
                        }
                    }
                    profile.LaunchFile = executable;
                    profile.LastProcessId = candidate;
                    return candidate;
                }
                await Task.Delay(500, cancellationToken);
            }
            if (ClientLaunchConfiguration.IsEnabled(template)) Terminate(root.Id);
            throw new TimeoutException($"No new lu4.bin process appeared within 90 seconds; " +
                (root.HasExited ? $"the launcher exited with code {root.ExitCode}" : "the launcher is still running"));
        }
        finally
        {
            pendingBroker?.Dispose();
            _launchGate.Release();
        }
    }

    public async Task WaitForGameWindowAsync(int pid, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        var stableLargeWindowSamples = 0;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        throw new TimeoutException("The game window did not appear within 3 minutes.");
    }

    private static IEnumerable<int> CurrentClientPids() =>
        Process.GetProcessesByName("lu4")
            .Concat(Process.GetProcessesByName("lu4.bin"))
            .Select(process => process.Id)
            .Distinct();

    private static async Task WaitForAgentReadyAsync(int pid, ClientAgentHookLoader.HookLease? hook, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsProcessAlive(pid)) throw new InvalidOperationException("The client exited before the HWID agent started.");
            try
            {
                using var ready = EventWaitHandle.OpenExisting($@"Local\PriceCheckAgentReady_{pid}");
                if (ready.WaitOne(0)) return;
            }
            catch (WaitHandleCannotBeOpenedException) { }
            hook?.Pulse();
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException("The HWID agent did not report readiness within 15 seconds.");
    }

    private static bool IsProcessAlive(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch { return false; }
    }

    private static DateTime ProcessStartTime(int pid)
    {
        try { return Process.GetProcessById(pid).StartTime.ToUniversalTime(); }
        catch { return DateTime.MinValue; }
    }

    private static long LargestVisibleWindowArea(int pid)
    {
        long largest = 0;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)pid || !GetWindowRect(window, out var rect)) return true;
            var width = Math.Max(0, rect.Right - rect.Left);
            var height = Math.Max(0, rect.Bottom - rect.Top);
            largest = Math.Max(largest, (long)width * height);
            return true;
        }, IntPtr.Zero);
        return largest;
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

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
}
