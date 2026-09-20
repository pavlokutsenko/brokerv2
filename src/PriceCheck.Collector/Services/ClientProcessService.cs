using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Contracts;
using System.Runtime.InteropServices;

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

    public void Terminate(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { }
        finally { _claimed.Remove(pid); }
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

    public async Task WaitForGameWindowAsync(int pid, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        var stableLargeWindowSamples = 0;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAlive(pid)) throw new InvalidOperationException("Клиент завершился во время проверки Active Anticheat");
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
        throw new TimeoutException("Большое окно клиента не появилось за 3 минуты");
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

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
}
