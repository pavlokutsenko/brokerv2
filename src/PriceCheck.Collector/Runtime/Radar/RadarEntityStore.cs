using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Runtime.Radar;

public sealed class RadarEntityStore
{
    private static readonly TimeSpan PresenceGrace = TimeSpan.FromSeconds(3);
    private readonly object _gate = new();
    private readonly Dictionary<int, Entity> _entities = [];
    private readonly Dictionary<string, Trader> _traders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _traderKeysByObjectId = [];
    private MarketZone? _zone;
    private PlayerPosition? _lastPlayerPosition;
    private bool _insideZone;
    private bool _collectionRequested;
    private bool _catalogEnabled;
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

    internal void SetObservationZone(MarketZone? zone, PlayerPosition? playerPosition, bool collectionRequested)
    {
        lock (_gate)
        {
            if (playerPosition is not null) _lastPlayerPosition = playerPosition;
            MarketZone? normalized = zone is MarketZone value && double.IsFinite(value.X) && double.IsFinite(value.Y) &&
                double.IsFinite(value.Radius) && value.Radius is >= 100 and <= 20_000 ? value : null;
            var wasEnabled = _catalogEnabled;
            _zone = normalized;
            _collectionRequested = collectionRequested;
            _insideZone = normalized is not null && _lastPlayerPosition is PlayerPosition player &&
                Distance(player.X, player.Y, normalized.Value.X, normalized.Value.Y) <= normalized.Value.Radius;
            _catalogEnabled = _collectionRequested && _insideZone;
            if (!wasEnabled && _catalogEnabled) RebuildVisibleCatalog();
            if (wasEnabled && !_catalogEnabled) ClearPendingAbsenceChecks();
        }
    }

    public void Apply(WorldPacket packet)
    {
        lock (_gate)
        {
            switch (packet)
            {
                case CharacterPacket value:
                    _entities[value.ObjectId] = new Entity(value.ObjectId, value.Name, value.KioskType, value.X, value.Y, value.Z);
                    if (_catalogEnabled) ApplyCharacter(value);
                    break;
                case MovePacket value when _entities.TryGetValue(value.ObjectId, out var current):
                    _entities[value.ObjectId] = current with { X = value.X, Y = value.Y, Z = value.Z };
                    if (_catalogEnabled && _traderKeysByObjectId.TryGetValue(value.ObjectId, out var traderKey) &&
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
                    if (_catalogEnabled && _traderKeysByObjectId.Remove(value.ObjectId, out var deletedKey) &&
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
            if (_zone is not null && _catalogEnabled)
                ReconcileMissingTraders(DateTimeOffset.UtcNow);
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
                CenterZoneConfigured = _zone is not null,
                IsInsideCenterZone = _insideZone,
                CollectionRequested = _collectionRequested,
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

    private void ReconcileMissingTraders(DateTimeOffset now)
    {
        foreach (var (key, trader) in _traders.ToArray())
        {
            if (!trader.IsTrading || trader.IsVisible) continue;
            if (trader.MissingInRangeSinceUtc is null)
                _traders[key] = trader with { MissingInRangeSinceUtc = now };
            else if (now - trader.MissingInRangeSinceUtc >= PresenceGrace)
                _traders[key] = trader with { IsTrading = false, MissingInRangeSinceUtc = null };
        }
    }

    private void RebuildVisibleCatalog()
    {
        _traderKeysByObjectId.Clear();
        foreach (var (key, trader) in _traders.ToArray())
            _traders[key] = trader with { IsVisible = false, MissingInRangeSinceUtc = null };
        foreach (var entity in _entities.Values)
            ApplyCharacter(new CharacterPacket(entity.ObjectId, entity.Name, string.Empty,
                entity.KioskType, entity.X, entity.Y, entity.Z));
    }

    private void ClearPendingAbsenceChecks()
    {
        foreach (var (key, trader) in _traders.ToArray())
            if (trader.MissingInRangeSinceUtc is not null)
                _traders[key] = trader with { MissingInRangeSinceUtc = null };
    }

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private sealed record Entity(int ObjectId, string Name, int KioskType, int X, int Y, int Z);
    private sealed record Trader(int ObjectId, string Name, int KioskType, int X, int Y, int Z,
        bool IsVisible, bool IsTrading, DateTimeOffset LastSeenAtUtc, DateTimeOffset? MissingInRangeSinceUtc);
}
