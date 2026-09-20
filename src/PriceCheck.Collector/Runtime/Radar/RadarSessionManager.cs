using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Runtime.Radar;

public sealed class RadarSessionManager : IAsyncDisposable
{
    private readonly Dictionary<int, RadarSession> _sessions = [];

    public async Task StartAsync(int pid, CancellationToken cancellationToken)
    {
        if (_sessions.ContainsKey(pid)) return;
        _sessions.Add(pid, await RadarSession.StartAsync(pid, cancellationToken));
    }

    public RadarSnapshot? Snapshot(int pid) => _sessions.TryGetValue(pid, out var session) ? session.Snapshot() : null;

    public async Task StopAsync(int pid)
    {
        if (!_sessions.Remove(pid, out var session)) return;
        await session.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values) await session.DisposeAsync();
        _sessions.Clear();
    }
}
