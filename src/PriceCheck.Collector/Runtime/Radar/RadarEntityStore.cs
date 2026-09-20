using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Runtime.Radar;

public sealed class RadarEntityStore
{
    private const double ConfirmedPresenceRadius = 2_000;
    private static readonly TimeSpan PresenceGrace = TimeSpan.FromSeconds(3);
    private readonly object _gate = new();
    private readonly Dictionary<int, Entity> _entities = [];
    private readonly Dictionary<string, Trader> _traders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _traderKeysByObjectId = [];
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

    public void Apply(WorldPacket packet)
    {
        lock (_gate)
        {
            switch (packet)
            {
                case CharacterPacket value:
                    _entities[value.ObjectId] = new Entity(value.ObjectId, value.Name, value.KioskType, value.X, value.Y, value.Z);
                    ApplyCharacter(value);
                    break;
                case MovePacket value when _entities.TryGetValue(value.ObjectId, out var current):
                    _entities[value.ObjectId] = current with { X = value.X, Y = value.Y, Z = value.Z };
                    if (_traderKeysByObjectId.TryGetValue(value.ObjectId, out var traderKey) &&
                        _traders.TryGetValue(traderKey, out var movingTrader))
                        _traders[traderKey] = movingTrader with
                        {
                            X = value.X,
                            Y = value.Y,
                            Z = value.Z,
                            LastSeenAtUtc = DateTimeOffset.UtcNow
                        };
                    break;
                case DeletePacket value:
                    _entities.Remove(value.ObjectId);
                    if (_traderKeysByObjectId.Remove(value.ObjectId, out var deletedKey) &&
                        _traders.TryGetValue(deletedKey, out var deletedTrader))
                        _traders[deletedKey] = deletedTrader with { IsVisible = false };
                    break;
            }
            _updatedAt = DateTimeOffset.UtcNow;
        }
    }

    internal RadarSnapshot Snapshot(int pid, string? playerName, PlayerPosition? livePlayer)
    {
        lock (_gate)
        {
            if (livePlayer is PlayerPosition position)
                ReconcileMissingTraders(position, DateTimeOffset.UtcNow);
            var traders = _traders.Values.Where(value => value.IsTrading).ToArray();
            var player = string.IsNullOrWhiteSpace(playerName) ? null : _entities.Values.FirstOrDefault(
                value => string.Equals(value.Name, playerName, StringComparison.OrdinalIgnoreCase));
            var centerX = livePlayer?.X ?? player?.X ?? (traders.Length == 0 ? 0 : traders.Average(value => (double)value.X));
            var centerY = livePlayer?.Y ?? player?.Y ?? (traders.Length == 0 ? 0 : traders.Average(value => (double)value.Y));
            return new RadarSnapshot
            {
                ProcessId = pid,
                PlayerX = centerX,
                PlayerY = centerY,
                PositionedActors = _entities.Count,
                VisibleTraders = traders.Count(value => value.IsVisible),
                Traders = traders.Select(value => new RadarPoint(
                    value.ObjectId, value.Name, value.KioskType, value.X, value.Y,
                    Math.Sqrt(Math.Pow(value.X - centerX, 2) + Math.Pow(value.Y - centerY, 2)),
                    value.IsVisible, value.LastSeenAtUtc)).ToArray(),
                CapturedAtUtc = _updatedAt
            };
        }
    }

    private void ApplyCharacter(CharacterPacket value)
    {
        var key = Normalize(value.Name);
        if (key.Length == 0) return;
        var now = DateTimeOffset.UtcNow;
        if (value.KioskType is 1 or 3 or 8)
        {
            if (_traderKeysByObjectId.TryGetValue(value.ObjectId, out var previousKey) && previousKey != key)
                _traderKeysByObjectId.Remove(value.ObjectId);
            _traderKeysByObjectId[value.ObjectId] = key;
            _traders[key] = new Trader(value.ObjectId, value.Name, value.KioskType,
                value.X, value.Y, value.Z, true, true, now, null);
            return;
        }

        if (_traders.TryGetValue(key, out var known))
            _traders[key] = known with
            {
                ObjectId = value.ObjectId,
                IsVisible = true,
                IsTrading = false,
                LastSeenAtUtc = now,
                MissingInRangeSinceUtc = null
            };
    }

    private void ReconcileMissingTraders(PlayerPosition player, DateTimeOffset now)
    {
        foreach (var (key, trader) in _traders.ToArray())
        {
            if (!trader.IsTrading || trader.IsVisible) continue;
            var distance = Math.Sqrt(Math.Pow(trader.X - player.X, 2) + Math.Pow(trader.Y - player.Y, 2));
            if (distance > ConfirmedPresenceRadius)
            {
                if (trader.MissingInRangeSinceUtc is not null)
                    _traders[key] = trader with { MissingInRangeSinceUtc = null };
                continue;
            }

            if (trader.MissingInRangeSinceUtc is null)
                _traders[key] = trader with { MissingInRangeSinceUtc = now };
            else if (now - trader.MissingInRangeSinceUtc >= PresenceGrace)
                _traders[key] = trader with { IsTrading = false, MissingInRangeSinceUtc = null };
        }
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private sealed record Entity(int ObjectId, string Name, int KioskType, int X, int Y, int Z);
    private sealed record Trader(int ObjectId, string Name, int KioskType, int X, int Y, int Z,
        bool IsVisible, bool IsTrading, DateTimeOffset LastSeenAtUtc, DateTimeOffset? MissingInRangeSinceUtc);
}
