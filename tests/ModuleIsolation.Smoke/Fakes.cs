using PriceCheck.Collection;
using PriceCheck.Contracts;
using PriceCheck.Collector.Contracts;
using PriceCheck.Collector.Models;

internal sealed class FakeProcesses : IClientProcessService
{
    private readonly Dictionary<int, ClientSession> _sessions = [];
    private int _next = 100;
    public HashSet<int> Proxies { get; } = [];
    public int Terminations { get; private set; }
    public int Releases { get; private set; }
    public ClientSession? Identity(int pid) => _sessions.GetValueOrDefault(pid);
    public bool IsAlive(int pid) => _sessions.ContainsKey(pid);
    public Task<int> LaunchAndBindAsync(CollectorProfile profile, LaunchTemplate? template, CancellationToken token)
    {
        var pid = _next++;
        _sessions.Add(pid, new(pid, DateTimeOffset.UtcNow));
        Proxies.Add(pid);
        return Task.FromResult(pid);
    }
    public Task WaitForGameWindowAsync(int pid, CancellationToken token) => Task.CompletedTask;
    public Task ActivateLateAgentAsync(int pid, CancellationToken token) => Task.CompletedTask;
    public void Terminate(int pid) { Terminations++; _sessions.Remove(pid); Proxies.Remove(pid); }
    public void Release(int? pid) { Releases++; if (pid is int value) Proxies.Remove(value); }
    public void ReusePid(int pid) => _sessions[pid] = new(pid, _sessions[pid].StartedAtUtc.AddSeconds(1));
}

internal sealed class FakeRadar : IRadarSessions
{
    public int Starts { get; private set; }
    public HashSet<int> Active { get; } = [];
    public bool FailStart { get; set; }
    public TaskCompletionSource? PendingStart { get; set; }
    public RadarSnapshot? CurrentSnapshot { get; set; }
    public async Task StartAsync(int pid, MarketZone? zone, bool collectionEnabled, CancellationToken token)
    {
        Starts++;
        if (FailStart) throw new InvalidOperationException("Synthetic receive signature mismatch.");
        if (PendingStart is not null) await PendingStart.Task;
        Active.Add(pid);
    }
    public RadarSnapshot? Snapshot(int pid, MarketZone? zone, bool collectionEnabled) => CurrentSnapshot;
    public Task StopAsync(int pid) { Active.Remove(pid); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { Active.Clear(); return ValueTask.CompletedTask; }
}

internal sealed class PendingWorker : ICollectionWorker
{
    public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Runs { get; private set; }
    public Task RunAsync(ClientSession session, string mode, string output, Action<System.Diagnostics.ProcessStartInfo> configure)
    {
        Runs++;
        return Complete.Task;
    }
}
