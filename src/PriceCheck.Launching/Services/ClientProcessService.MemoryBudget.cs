using System.Collections.Concurrent;
using PriceCheck.Contracts;
using PriceCheck.Collector.Runtime.Driver;
using PriceCheck.Windows;

namespace PriceCheck.Collector.Services;

public sealed partial class ClientProcessService
{
    private readonly ConcurrentDictionary<int, (ClientSession Session, WorkingSetBounds Original)> _memoryBudgets = new();

    public Task ApplyMemoryBudgetAsync(ClientSession session, int maximumMiB, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!_claimed.ContainsKey(session.ProcessId) || !ClientProcessIdentity.IsCurrent(session))
            throw new IOException("ОЗУ: клиент больше не принадлежит этому запуску.");
        if (_resourceJobs.TryGetValue(session.ProcessId, out var job))
        {
            if (!job.Contains(session.ProcessId)) throw new IOException("ОЗУ: клиент вышел из лимитирующего задания.");
            var actual = Lu4Device.ReadWorkingSetBounds(session);
            if (actual.MaximumBytes != checked((ulong)maximumMiB * 1024 * 1024) ||
                (actual.Flags & 12) != 4 || (actual.Flags & 3) != 2)
                throw new IOException("ОЗУ: Windows не подтвердила лимит игрового клиента.");
            LogMemoryBudget(session, $"job-applied maximumMiB={maximumMiB} cpuPercent={job.ReadCpuPercent()?.ToString() ?? "off"}");
            return;
        }
        if (_memoryBudgets.ContainsKey(session.ProcessId)) throw new InvalidOperationException("Memory budget already applied.");
        try
        {
            using var device = new Lu4Device();
            var original = device.ApplyWorkingSetBudget(session, maximumMiB);
            _memoryBudgets[session.ProcessId] = (session, original);
            LogMemoryBudget(session, $"applied maximumMiB={maximumMiB}");
        }
        catch (Exception error)
        {
            LogMemoryBudget(session, $"apply-failed {error.Message}");
            throw new IOException("ОЗУ: драйвер не подтвердил лимит. Нужна сборка с поддержкой рабочего набора. " + error.Message, error);
        }
    }, token);

    private void RestoreMemoryBudget(int pid)
    {
        if (!_memoryBudgets.TryRemove(pid, out var budget) || !ClientProcessIdentity.IsCurrent(budget.Session)) return;
        try
        {
            using var device = new Lu4Device();
            device.RestoreWorkingSetBudget(budget.Session, budget.Original);
            LogMemoryBudget(budget.Session, "restored");
        }
        catch (Exception error) { LogMemoryBudget(budget.Session, $"restore-failed {error.Message}"); }
    }

    private static void LogMemoryBudget(ClientSession session, string result)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceCheckCollector", "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "memory-budget.log"),
                $"{DateTimeOffset.UtcNow:O} pid={session.ProcessId} birth={session.StartedAtUtc:O} {result}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
