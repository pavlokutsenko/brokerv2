using System.Collections.Concurrent;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Collector.Services;

internal sealed partial class ProxyTcpBroker
{
    private readonly bool _guarded;
    private readonly LaunchTemplate _probeTemplate;
    private readonly ConcurrentDictionary<int,byte> _boundPids = new();
    private long _connections, _sentBytes, _receivedBytes, _worldSent, _worldReceived, _worldOpenedAt, _worldConnections;
    private int _worldActive;
    private string? _error;
    public bool AllowLogin { get; set; }
    public string? Error => Volatile.Read(ref _error) ?? (_acceptLoop.IsFaulted ? "Локальный прокси перестал принимать соединения." : null);
    public long Connections => Interlocked.Read(ref _connections);
    public long SentBytes => Interlocked.Read(ref _sentBytes);
    public long ReceivedBytes => Interlocked.Read(ref _receivedBytes);
    public long WorldConnections => Interlocked.Read(ref _worldConnections);
    public long? WorldOpenedAt => Interlocked.Read(ref _worldOpenedAt) is >0 and var tick ? tick : null;
    public bool WorldTrafficConfirmed => Volatile.Read(ref _worldActive) > 0 &&
        Interlocked.Read(ref _worldSent) > 0 && Interlocked.Read(ref _worldReceived) > 0;
    public Task VerifyUpstreamAsync(CancellationToken token) => VerifyUpstreamAsync(_probeTemplate, token);
    private void SetError(string error) { if (_guarded) Interlocked.CompareExchange(ref _error, error, null); }
    private bool OwnsRedirect(int pid)
    {
        if (_boundPids.ContainsKey(pid)) return true;
        if (!_guarded) return false;
        using var device = new Lu4Device();
        var status = device.QueryProxyGuard(pid);
        return status.Active && status.HostProcessId == Environment.ProcessId && status.ListenerPort == ListenerPort;
    }
    public void BindAdditional(int pid)
    {
        using var device = new Lu4Device();
        var route = device.QueryProxyGuard(pid);
        if (!route.Active || route.HostProcessId != Environment.ProcessId || route.ListenerPort != ListenerPort)
            throw new LaunchProtectionException("Дочерний процесс не унаследовал защиту прокси.");
        _boundPids.TryAdd(pid, 0);
        Volatile.Write(ref _pid, pid);
    }
    private void CountTraffic(string category, int port, int count)
    {
        if (category == "client_to_proxy") {
            Interlocked.Add(ref _sentBytes, count);
            if (port == 7782) Interlocked.Add(ref _worldSent, count);
        } else {
            Interlocked.Add(ref _receivedBytes, count);
            if (port == 7782) Interlocked.Add(ref _worldReceived, count);
        }
    }
}
