using System.Text.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed class LocalPriceQueue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Dictionary<string, Entry>> _profiles = [];
    private readonly Dictionary<Guid, string> _activeSectors = [];
    private readonly HashSet<Guid> _observedProfiles = [];

    public void Clear(Guid profileId)
    {
        lock (_gate)
        {
            _profiles.Remove(profileId);
            _activeSectors.Remove(profileId);
            _observedProfiles.Remove(profileId);
        }
    }

    public void Observe(CollectorProfile profile, RadarSnapshot radar)
    {
        lock (_gate)
        {
            var entries = Load(profile.Id);
            // As in the old collector, the first radar after a worker/app
            // start is the baseline. A relog changes every ObjectID and must
            // not turn the whole market into thousands of priority events.
            var firstObservation = _observedProfiles.Add(profile.Id);
            var changed = false;
            if (firstObservation)
            {
                // Old-collector priority events live only for the current
                // process session. Do not restore stale route interruptions
                // from the durable queue after an application restart.
                foreach (var (key, entry) in entries.ToArray())
                {
                    if (entry.Priority == 0) continue;
                    entries[key] = entry with { Priority = 0 };
                    changed = true;
                }
            }
            // Only the saved centre has authoritative coverage of the entire
            // market. While roaming we merge observations without declaring
            // off-radar traders gone.
            var visibleKeys = radar.Traders.Where(value => value.IsVisible)
                .Select(value => value.Name.Trim().ToUpperInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var previouslyVisible = entries.Values.Count(value => value.WasVisible);
            // A receive-hook recovery can briefly expose only a fragment of
            // the market even though the player is inside the saved centre.
            // Do not turn that cold fragment into thousands of departures.
            var authoritative = radar.IsInsideCenterZone &&
                (previouslyVisible < 100 || visibleKeys.Count >= previouslyVisible / 2);
            var authoritativeKeys = authoritative ? visibleKeys : null;
            foreach (var trader in radar.Traders.Where(value => value.IsVisible))
            {
                var key = trader.Name.Trim().ToUpperInvariant();
                if (!entries.TryGetValue(key, out var entry))
                {
                    entries[key] = new Entry(key, trader.Name, trader.ObjectId, trader.KioskType,
                        trader.X, trader.Y, true, true, firstObservation ? 0 : 100, 0, DateTimeOffset.MinValue, null);
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
                    Priority = !firstObservation && (reappeared || newSession) ? 100 : entry.Priority,
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

    public LocalPricePlan? PlanNext(Guid profileId, RadarSnapshot radar, double captureRadius, int batchLimit)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var liveObjects = radar.Traders.Where(value => value.IsVisible)
                .Select(value => ((long)value.ObjectId, value.Name.Trim().ToUpperInvariant())).ToHashSet();
            var ready = Load(profileId).Values
                .Where(value => value.WasVisible && value.Pending && value.AvailableAtUtc <= now && liveObjects.Contains((value.ObjectId, value.TraderKey)))
                .ToArray();
            var selected = SelectNext(profileId, radar, ready);
            if (selected is null) return null;

            var distance = Distance(selected.X, selected.Y, radar.PlayerX, radar.PlayerY);
            if (distance > captureRadius)
                return new LocalPricePlan(ToTarget(selected), []);

            // Preserve the old collector's route decision. Fast driver reads
            // are an execution optimization inside the selected sector, not a
            // second planner that is allowed to pull the route elsewhere.
            var selectedSector = SectorKey(selected.X, selected.Y);
            var batch = ready
                .Where(value => SectorKey(value.X, value.Y) == selectedSector)
                .Select(value => new
                {
                    Entry = value,
                    Distance = Distance(value.X, value.Y, radar.PlayerX, radar.PlayerY)
                })
                .Where(value => value.Distance <= captureRadius)
                .OrderByDescending(value => value.Entry.Priority)
                .ThenBy(value => value.Distance)
                .ThenBy(value => value.Entry.TraderKey, StringComparer.OrdinalIgnoreCase)
                .Take(batchLimit)
                .Select(value => ToTarget(value.Entry))
                .ToArray();
            return new LocalPricePlan(ToTarget(selected), batch);
        }
    }

    private Entry? SelectNext(Guid profileId, RadarSnapshot radar, Entry[] ready)
    {
        // The old collector's stable traversal policy: live priority events
        // interrupt the route, while ordinary warm-up remains in one 420-unit
        // sector until it is drained. Then choose the nearest remaining sector
        // and the nearest trader inside it.
        var entry = ready.Where(value => value.Priority > 0)
            .OrderByDescending(value => value.Priority)
            .ThenBy(value => Distance(value.X, value.Y, radar.PlayerX, radar.PlayerY))
            .ThenBy(value => value.TraderKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (entry is not null) return entry;

        var active = _activeSectors.GetValueOrDefault(profileId);
        if (active is null || !ready.Any(value => SectorKey(value.X, value.Y) == active))
        {
            active = ready.GroupBy(value => SectorKey(value.X, value.Y))
                .Select(group => new
                {
                    Key = group.Key,
                    X = group.Average(value => value.X),
                    Y = group.Average(value => value.Y)
                })
                .OrderBy(value => Distance(value.X, value.Y, radar.PlayerX, radar.PlayerY))
                .ThenBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => value.Key)
                .FirstOrDefault();
            if (active is not null)
            {
                _activeSectors[profileId] = active;
            }
        }

        return active is null ? null : ready
            .Where(value => SectorKey(value.X, value.Y) == active)
            .OrderBy(value => Distance(value.X, value.Y, radar.PlayerX, radar.PlayerY))
            .ThenBy(value => value.TraderKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
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
                // The old collector defers a failed trader for a later cycle;
                // it does not turn a timeout into a route-interrupting event.
                // Keep it pending, but let new/reopened traders retain the
                // only high-priority lane.
                Pending = true, Priority = 0, AttemptCount = attempts,
                AvailableAtUtc = DateTimeOffset.UtcNow.AddSeconds(
                    Math.Min(900, 120 * Math.Pow(2, Math.Min(attempts - 1, 3))))
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
    private static string SectorKey(double x, double y) => $"s:{Math.Floor(x / 420.0)}:{Math.Floor(y / 420.0)}";
    private static LocalPriceTarget ToTarget(Entry value) => new(value.TraderKey, value.TraderName, value.ObjectId, value.KioskType, value.X, value.Y, value.Priority, value.AttemptCount);

    public sealed record Entry(
        string TraderKey, string TraderName, long ObjectId, int KioskType, double X, double Y,
        bool WasVisible, bool Pending, int Priority, int AttemptCount,
        DateTimeOffset AvailableAtUtc, DateTimeOffset? LastCheckedUtc);
}
