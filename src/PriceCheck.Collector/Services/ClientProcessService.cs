using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Contracts;

namespace PriceCheck.Collector.Services;

public sealed class ClientProcessService : IClientProcessService
{
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly HashSet<int> _claimed = [];

    public bool IsAlive(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch { return false; }
    }

    public void Release(int? pid)
    {
        if (pid is int value) _claimed.Remove(value);
    }

    public bool TryClaim(int pid)
    {
        if (!IsAlive(pid) || _claimed.Contains(pid)) return false;
        _claimed.Add(pid);
        return true;
    }

    public async Task<int> LaunchAndBindAsync(CollectorProfile profile, CancellationToken cancellationToken)
    {
        await _launchGate.WaitAsync(cancellationToken);
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
            _ = Process.Start(start) ?? throw new InvalidOperationException("Windows не запустил клиент");

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
                    profile.LaunchFile = executable;
                    profile.LastProcessId = candidate;
                    return candidate;
                }
                await Task.Delay(500, cancellationToken);
            }
            throw new TimeoutException("Новый процесс lu4.bin не появился за 90 секунд");
        }
        finally
        {
            _launchGate.Release();
        }
    }

    public int AttachNewestUnclaimed()
    {
        var pid = CurrentClientPids()
            .Where(value => !_claimed.Contains(value))
            .OrderByDescending(ProcessStartTime)
            .FirstOrDefault();
        if (pid == 0) throw new InvalidOperationException("Свободный процесс lu4.bin не найден");
        _claimed.Add(pid);
        return pid;
    }

    private static IEnumerable<int> CurrentClientPids() =>
        Process.GetProcessesByName("lu4")
            .Concat(Process.GetProcessesByName("lu4.bin"))
            .Select(process => process.Id)
            .Distinct();

    private static DateTime ProcessStartTime(int pid)
    {
        try { return Process.GetProcessById(pid).StartTime.ToUniversalTime(); }
        catch { return DateTime.MinValue; }
    }

    private static string ResolveLaunchFile(CollectorProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.LaunchFile) && File.Exists(profile.LaunchFile))
            return profile.LaunchFile;
        if (string.IsNullOrWhiteSpace(profile.ClientFolder) || !Directory.Exists(profile.ClientFolder))
            throw new DirectoryNotFoundException("Сначала выберите папку клиента");

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
        throw new FileNotFoundException("В выбранной папке не найден lu4.bin или launcher.exe");
    }
}
