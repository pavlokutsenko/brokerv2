using PriceCheck.Collector.Models;
using PriceCheck.Collector.Runtime.Driver;
using System.Diagnostics;

namespace PriceCheck.Collector.Runtime.Radar;

public sealed class RadarSession : IAsyncDisposable
{
    private readonly int _pid;
    private readonly Lu4Device _device;
    private readonly ReceiveHookSession _hook;
    private readonly RadarEntityStore _store = new();
    private readonly LocalPlayerPositionReader? _playerPosition;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _readerTask;

    private RadarSession(int pid, Lu4Device device, ReceiveHookSession hook, MarketZone? zone, bool collectionEnabled)
    {
        _pid = pid;
        _device = device;
        _hook = hook;
        try { _playerPosition = new LocalPlayerPositionReader(device, pid); }
        catch (InvalidOperationException) { }
        catch (InvalidDataException) { }
        _store.SetObservationZone(zone, null, collectionEnabled);
        var reader = new PacketRingReader(device, pid, hook.RingAddress, hook.DropCounterAddress);
        _readerTask = Task.Run(() => reader.RunAsync(OnPacket, _stop.Token));
    }

    public static async Task<RadarSession> StartAsync(
        int pid, MarketZone? zone, bool collectionEnabled, CancellationToken cancellationToken)
    {
        Exception? last = null;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Lu4Device? device = null;
            try
            {
                device = new Lu4Device();
                var hook = ReceiveHookSession.Install(device, pid);
                return new RadarSession(pid, device, hook, zone, collectionEnabled);
            }
            catch (Exception exception)
            {
                last = exception;
                device?.Dispose();
                await Task.Delay(250, cancellationToken);
            }
        }
        throw new TimeoutException($"Receive-hook не установился за 60 секунд: {last?.Message}", last);
    }

    public RadarSnapshot Snapshot(MarketZone? zone, bool collectionEnabled)
    {
        if (_readerTask.IsFaulted)
            throw new InvalidOperationException("Packet radar остановился", _readerTask.Exception?.GetBaseException());
        string? playerName = null;
        try
        {
            var title = Process.GetProcessById(_pid).MainWindowTitle;
            var separator = title.LastIndexOf(" - ", StringComparison.Ordinal);
            if (separator >= 0 && separator + 3 < title.Length) playerName = title[(separator + 3)..].Trim();
        }
        catch { }
        PlayerPosition? livePlayer = null;
        if (_playerPosition?.TryRead(out var position) == true) livePlayer = position;
        _store.SetObservationZone(zone, livePlayer, collectionEnabled);
        return _store.Snapshot(_pid, playerName, livePlayer);
    }

    private void OnPacket(ReadOnlyMemory<byte> data)
    {
        var decoded = WorldPacketDecoder.Decode(data.Span);
        if (decoded is not null) _store.Apply(decoded);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _readerTask; } catch (OperationCanceledException) { } catch { }
        _hook.Dispose();
        _device.Dispose();
        _stop.Dispose();
    }
}
