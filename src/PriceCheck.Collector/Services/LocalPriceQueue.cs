using System.Text.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed class LocalPriceQueue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Dictionary<string, Entry>> _profiles = [];

    public void Observe(CollectorProfile profile, RadarSnapshot radar)
    {
        lock (_gate)
        {
            var entries = Load(profile.Id);
            // Only the saved centre has authoritative coverage of the entire
            // market. While roaming we merge observations without declaring
            // off-radar traders gone.
            var authoritativeKeys = radar.IsInsideCenterZone
                ? radar.Traders.Select(value => value.Name.Trim().ToUpperInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null;
            var changed = false;
            foreach (var trader in radar.Traders.Where(value => value.IsVisible))
            {
                var key = trader.Name.Trim().ToUpperInvariant();
                if (!entries.TryGetValue(key, out var entry))
                {
                    entries[key] = new Entry(key, trader.Name, trader.ObjectId, trader.KioskType,
                        trader.X, trader.Y, true, true, 100, 0, DateTimeOffset.MinValue, null);
                    changed = true;
                    continue;
                }
                var reappeared = !entry.WasVisible;
                var newSession = entry.ObjectId != trader.ObjectId;
                var updated = entry with
                {
                    TraderName = trader.Name, ObjectId = trader.ObjectId, KioskType = trader.KioskType,
                    X = trader.X, Y = trader.Y, WasVisible = true,
                    Pending = entry.Pending || reappeared || newSession,
                    Priority = reappeared || newSession ? 100 : entry.Priority,
                    AttemptCount = reappeared || newSession ? 0 : entry.AttemptCount,
                    AvailableAtUtc = reappeared || newSession ? DateTimeOffset.MinValue : entry.AvailableAtUtc,
                };
                if (updated != entry) { entries[key] = updated; changed = true; }
            }
            foreach (var (key, entry) in entries.ToArray())
            {
                if (authoritativeKeys is not null && !authoritativeKeys.Contains(key) && entry.WasVisible)
                {
                    entries[key] = entry with { WasVisible = false };
                    changed = true;
                }
            }
            if (changed) Save(profile.Id, entries);
        }
    }

    public IReadOnlyList<LocalPriceTarget> Nearby(Guid profileId, RadarSnapshot radar, double radius, int limit)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            return Load(profileId).Values
                .Where(value => value.WasVisible && value.Pending && value.AvailableAtUtc <= now)
                .Select(value => new { Entry = value, Distance = Distance(value.X, value.Y, radar.PlayerX, radar.PlayerY) })
                .Where(value => value.Distance <= radius)
                .OrderByDescending(value => value.Entry.Priority)
                .ThenBy(value => value.Distance)
                .ThenBy(value => value.Entry.TraderKey, StringComparer.OrdinalIgnoreCase)
                .Take(limit).Select(value => ToTarget(value.Entry)).ToArray();
        }
    }

    public LocalPriceTarget? Next(Guid profileId, RadarSnapshot radar)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var entry = Load(profileId).Values
                .Where(value => value.WasVisible && value.Pending && value.AvailableAtUtc <= now)
                .OrderByDescending(value => value.Priority)
                .ThenBy(value => Distance(value.X, value.Y, radar.PlayerX, radar.PlayerY))
                .ThenBy(value => value.TraderKey, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            return entry is null ? null : ToTarget(entry);
        }
    }

    public void Complete(Guid profileId, string traderKey)
    {
        lock (_gate)
        {
            var entries = Load(profileId);
            if (!entries.TryGetValue(traderKey, out var entry)) return;
            entries[traderKey] = entry with { Pending = false, Priority = 0, AttemptCount = 0, LastCheckedUtc = DateTimeOffset.UtcNow };
            Save(profileId, entries);
        }
    }

    public void Fail(Guid profileId, string traderKey)
    {
        lock (_gate)
        {
            var entries = Load(profileId);
            if (!entries.TryGetValue(traderKey, out var entry)) return;
            var attempts = entry.AttemptCount + 1;
            entries[traderKey] = entry with
            {
                Pending = true, Priority = 70, AttemptCount = attempts,
                AvailableAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(600, 15 * Math.Pow(2, Math.Min(attempts - 1, 5))))
            };
            Save(profileId, entries);
        }
    }

    private Dictionary<string, Entry> Load(Guid profileId)
    {
        if (_profiles.TryGetValue(profileId, out var existing)) return existing;
        var path = PathFor(profileId);
        Dictionary<string, Entry>? loaded = null;
        try { if (File.Exists(path)) loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path), Json); }
        catch (IOException) { }
        catch (JsonException) { }
        var result = loaded is null
            ? new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Entry>(loaded, StringComparer.OrdinalIgnoreCase);
        _profiles[profileId] = result;
        return result;
    }

    private static void Save(Guid profileId, Dictionary<string, Entry> entries)
    {
        var path = PathFor(profileId); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, Json));
        File.Move(temporary, path, true);
    }

    private static string PathFor(Guid profileId) => Path.Combine(AppContext.BaseDirectory, "data", "local-price-queue", $"{profileId:N}.json");
    private static double Distance(double x1, double y1, double x2, double y2) => Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));
    private static LocalPriceTarget ToTarget(Entry value) => new(value.TraderKey, value.TraderName, value.ObjectId, value.KioskType, value.X, value.Y, value.Priority, value.AttemptCount);

    public sealed record Entry(
        string TraderKey, string TraderName, long ObjectId, int KioskType, double X, double Y,
        bool WasVisible, bool Pending, int Priority, int AttemptCount,
        DateTimeOffset AvailableAtUtc, DateTimeOffset? LastCheckedUtc);
}
