using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Runtime.Radar;

public sealed class RadarEntityStore
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Entity> _entities = [];
    private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

    public void Apply(WorldPacket packet)
    {
        lock (_gate)
        {
            switch (packet)
            {
                case CharacterPacket value:
                    _entities[value.ObjectId] = new Entity(value.ObjectId, value.Name, value.KioskType, value.X, value.Y, value.Z);
                    break;
                case MovePacket value when _entities.TryGetValue(value.ObjectId, out var current):
                    _entities[value.ObjectId] = current with { X = value.X, Y = value.Y, Z = value.Z };
                    break;
                case DeletePacket value:
                    _entities.Remove(value.ObjectId);
                    break;
            }
            _updatedAt = DateTimeOffset.UtcNow;
        }
    }

    internal RadarSnapshot Snapshot(int pid, string? playerName, PlayerPosition? livePlayer)
    {
        lock (_gate)
        {
            var traders = _entities.Values.Where(value => value.KioskType is 1 or 3 or 8).ToArray();
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
                Traders = traders.Select(value => new RadarPoint(
                    value.ObjectId, value.Name, value.KioskType, value.X, value.Y,
                    Math.Sqrt(Math.Pow(value.X - centerX, 2) + Math.Pow(value.Y - centerY, 2)))).ToArray(),
                CapturedAtUtc = _updatedAt
            };
        }
    }

    private sealed record Entity(int ObjectId, string Name, int KioskType, int X, int Y, int Z);
}
