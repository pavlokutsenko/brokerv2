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
    private readonly FileStream _ownership;
    private readonly object _positionGate = new();

    private RadarSession(int pid, Lu4Device device, ReceiveHookSession hook, MarketZone? zone, bool collectionEnabled, FileStream ownership)
    {
        _ownership = ownership;
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
        var process = PriceCheck.Windows.ClientProcessIdentity.Read(pid) ?? throw new InvalidOperationException("Client exited.");
        var ownership = PriceCheck.Windows.ClientReaderLease.Acquire(process);
        try
        {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PriceCheck.Windows.ClientProcessIdentity.IsCurrent(process)) throw new InvalidOperationException("Client changed while connecting the reader.");
            Lu4Device? device = null;
            try
            {
                device = new Lu4Device();
                var hook = ReceiveHookSession.Install(device, pid);
                return new RadarSession(pid, device, hook, zone, collectionEnabled, ownership);
            }
            catch (Exception exception)
            {
                last = exception;
                device?.Dispose();
                await Task.Delay(250, cancellationToken);
            }
        }
        throw new TimeoutException($"Receive hook was not installed within 60 seconds: {last?.Message}", last);
        }
        catch { ownership.Dispose(); throw; }
    }

    public RadarSnapshot Snapshot(MarketZone? zone, bool collectionEnabled)
    {
        if (_readerTask.IsFaulted)
            throw new InvalidOperationException("Packet radar stopped.", _readerTask.Exception?.GetBaseException());
        string? playerName = null;
        try
        {
            var title = Process.GetProcessById(_pid).MainWindowTitle;
            var separator = title.LastIndexOf(" - ", StringComparison.Ordinal);
            if (separator >= 0 && separator + 3 < title.Length) playerName = title[(separator + 3)..].Trim();
        }
        catch { }
        var livePlayer = ReadLivePosition();
        _store.SetObservationZone(zone, livePlayer, collectionEnabled);
        return _store.Snapshot(_pid, playerName, livePlayer);
    }

    public void RememberTraderKeys(IReadOnlyList<string> keys) => _store.RememberTraderKeys(keys);

    private void OnPacket(ReadOnlyMemory<byte> data)
    {
        var decoded = WorldPacketDecoder.Decode(data.Span);
        // Bind a closure to the observer's position now, not at a later UI tick.
        var observer = decoded is CharacterPacket { KioskType: 0 } character && _store.NeedsClosurePosition(character.Name)
            ? ReadLivePosition() : null;
        if (decoded is not null) _store.Apply(decoded, observerX: observer?.X, observerY: observer?.Y);
    }

    private PlayerPosition? ReadLivePosition()
    {
        lock (_positionGate)
            return _playerPosition?.TryRead(out var position) == true ? position : null;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _readerTask; } catch (OperationCanceledException) { } catch { }
        _hook.Dispose();
        _device.Dispose();
        _stop.Dispose();
        _ownership.Dispose();
    }
}
