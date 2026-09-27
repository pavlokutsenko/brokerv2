using PriceCheck.Collector.Models;
using System.Text;

namespace PriceCheck.Collector.Runtime.Radar;

// One reader/client owns this store. A delete is visibility loss, never closure.
public sealed class RadarEntityStore
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Entity> _entities = [];
    private readonly RadarIdentityCache _identities = new();
    private readonly Dictionary<string, RadarPoint> _traders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _traderKeysByObjectId = [];
    private MarketZone? _zone;
    private bool _collectionRequested;
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

    public bool NeedsClosurePosition(string name)
    {
        lock (_gate) return _traders.ContainsKey(Normalize(name));
    }

    internal void SetObservationZone(MarketZone? zone, PlayerPosition? playerPosition, bool collectionRequested)
    {
        lock (_gate)
        {
            _zone = zone is MarketZone value && double.IsFinite(value.X) && double.IsFinite(value.Y)
                ? value with { Radius = 500 } : null;
            _collectionRequested = collectionRequested;
        }
    }

    public void Apply(WorldPacket packet, DateTimeOffset? observedAt = null,
        double? observerX = null, double? observerY = null)
    {
        lock (_gate)
        {
            var now = observedAt ?? DateTimeOffset.UtcNow;
            _identities.Apply(packet, now);
            switch (packet)
            {
                case CharacterPacket value:
                    _entities[value.ObjectId] = new(value.Name, value.X, value.Y);
                    ApplyCharacter(value, now, observerX, observerY);
                    break;
                case MovePacket value:
                    if (_entities.TryGetValue(value.ObjectId, out var entity))
                        _entities[value.ObjectId] = entity with { X = value.X, Y = value.Y };
                    if (_traderKeysByObjectId.TryGetValue(value.ObjectId, out var key) &&
                        _traders.TryGetValue(key, out var trader) && trader.ObjectId == value.ObjectId)
                        _traders[key] = trader with { X = value.X, Y = value.Y, LastSeenAtUtc = now };
                    break;
                case DeletePacket value:
                    _entities.Remove(value.ObjectId);
                    if (_traderKeysByObjectId.TryGetValue(value.ObjectId, out var deletedKey) &&
                        _traders.TryGetValue(deletedKey, out var deleted) && deleted.ObjectId == value.ObjectId)
                        _traders[deletedKey] = deleted with { IsVisible = false };
                    break;
            }
            _updatedAt = now;
        }
    }

    internal RadarSnapshot Snapshot(int pid, string? playerName, PlayerPosition? livePlayer)
    {
        lock (_gate)
        {
            var player = string.IsNullOrWhiteSpace(playerName) ? null : _entities.Values.FirstOrDefault(
                value => string.Equals(value.Name, playerName, StringComparison.OrdinalIgnoreCase));
            var x = livePlayer?.X ?? player?.X ?? 0;
            var y = livePlayer?.Y ?? player?.Y ?? 0;
            var traders = _traders.Values.Where(value => Trading(value.KioskType)).ToArray();
            return new RadarSnapshot
            {
                ProcessId = pid,
                LivePlayerPositionAvailable = livePlayer is not null,
                WorldCharacterDataAvailable = _identities.Count > 0,
                PlayerX = x, PlayerY = y,
                CenterZoneConfigured = _zone is not null,
                IsInsideCenterZone = livePlayer is not null && _zone is MarketZone zone &&
                    Distance(x, y, zone.X, zone.Y) <= 500,
                CollectionRequested = _collectionRequested,
                CenterZoneX = _zone?.X ?? 0, CenterZoneY = _zone?.Y ?? 0,
                CenterZoneRadius = _zone?.Radius ?? 0,
                PositionedActors = _entities.Count,
                VisibleTraders = traders.Count(value => value.IsVisible),
                BrokerIdentities = _identities.Snapshot(),
                Traders = traders.Select(value => value with { Distance = Distance(value.X, value.Y, x, y) }).ToArray(),
                // Visibility loss cannot erase an already explicit closure.
                // Its original state time/observer position still govern confirmation.
                ClosedTraders = _traders.Values.Where(value => value.KioskType == 0).ToArray(),
                CapturedAtUtc = _updatedAt
            };
        }
    }

    private void ApplyCharacter(CharacterPacket value, DateTimeOffset now, double? observerX, double? observerY)
    {
        var key = Normalize(value.Name);
        if (key.Length is 0 or > 32 || value.ObjectId <= 0) return;
        _traders.TryGetValue(key, out var known);
        // Standing strangers do not become market traders. Their identities
        // still survive for broker joins throughout this client session.
        if (known is null && !Trading(value.KioskType)) return;
        if (_traderKeysByObjectId.TryGetValue(value.ObjectId, out var previousKey) && previousKey != key &&
            _traders.TryGetValue(previousKey, out var previous))
            _traders[previousKey] = previous with { IsVisible = false };
        if (known is not null && known.ObjectId != value.ObjectId) _traderKeysByObjectId.Remove(known.ObjectId);
        _traderKeysByObjectId[value.ObjectId] = key;
        var closed = known?.LastClosedAtUtc;
        var reopened = known?.LastReopenedAtUtc;
        var revision = known?.TradeRevision ?? 0;
        if (known is null || known.KioskType != value.KioskType) revision++;
        if (value.KioskType == 0 && known is not null && Trading(known.KioskType)) closed = now;
        // An ObjectID change or initial sighting in a new client is not reopen.
        if (Trading(value.KioskType) && known is not null && closed is not null &&
            (reopened is null || closed > reopened)) reopened = now;
        _traders[key] = new(value.ObjectId, value.Name, value.KioskType, value.X, value.Y, 0, true, now)
        {
            StateObservedAtUtc = now,
            StateObservedPlayerX = observerX, StateObservedPlayerY = observerY,
            LastClosedAtUtc = closed, LastReopenedAtUtc = reopened, TradeRevision = revision
        };
    }

    private static bool Trading(int kind) => kind is 1 or 3 or 8;
    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));
    private sealed record Entity(string Name, int X, int Y);
}
