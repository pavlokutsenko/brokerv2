using System.Text;
using System.Text.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed partial class CycleQueue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, CycleTraderState> _entries;
    private readonly HashSet<string> _boundThisSession = [];
    public static string Key(string name) => name.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceCheckCollector", "collection");

    public CycleQueue(CollectorProfile profile, Func<DateTimeOffset>? clock = null, string? directory = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        var scope = $"{profile.Id:N}-{Key(profile.Name)}-{Key(profile.City)}";
        if (scope.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Invalid market or city.");
        _path = Path.Combine(directory ?? Root, scope, "queue.json");
        _entries = Load();
    }

    public void Observe(RadarSnapshot radar)
    {
        var now = _clock(); var changed = false;
        foreach (var t in radar.Traders.Where(t => t.IsVisible && t.KioskType is 1 or 3 or 8))
        {
            var key = Key(t.Name);
            if (key.Length == 0) continue;
            // Only a broker observation admits a trader. Radar enriches the
            // coordinates/runtime binding of already known entries.
            if (!_entries.TryGetValue(key, out var e)) continue;
            var rebound = e.ObjectId != 0 && e.ObjectId != t.ObjectId;
            var moved = e.HasPosition && Distance(e.X, e.Y, t.X, t.Y) >= 20;
            var movedFromCheck = e.LastCheckedAt is not null && Distance(e.CheckedX, e.CheckedY, t.X, t.Y) >= 20;
            var reopened = e.KioskType != 0 && e.KioskType != t.KioskType;
            if (e.Active && ((_boundThisSession.Contains(key) && rebound) || reopened || moved || (movedFromCheck && !e.Dirty)))
            {
                Invalidate(e, moved || movedFromCheck ? "Shop moved" : "Shop reopened", now);
                changed = true;
            }
            if (e.ObjectId != t.ObjectId || e.X != t.X || e.Y != t.Y || !e.HasPosition) changed = true;
            e.Name = t.Name; e.ObjectId = t.ObjectId; e.KioskType = t.KioskType;
            e.X = t.X; e.Y = t.Y; e.HasPosition = true;
            _boundThisSession.Add(key);
        }
        if (changed) Save();
    }

    public IReadOnlyList<CycleTarget> Ready(double x, double y, int limit = 256)
    {
        var now = _clock();
        return _entries.Values.Where(e => e.Active && e.HasPosition && NeedsRead(e, now) && e.AvailableAt <= now)
            .OrderBy(e => Due(e)).ThenBy(e => Distance(x, y, e.X, e.Y))
            .Take(limit).Select(e => new CycleTarget(e.Key, e.Name, e.ObjectId, e.KioskType,
                e.X, e.Y, e.Revision, e.Dirty ? e.Reason : "24-hour check", Due(e))).ToArray();
    }

    public void Complete(CycleTarget target, DateTimeOffset capturedAt)
    {
        if (!_entries.TryGetValue(target.TraderKey, out var e) || !e.Active || e.Revision != target.Revision || e.ObjectId != target.ObjectId) return;
        e.LastCheckedAt = capturedAt; e.CheckedX = target.X; e.CheckedY = target.Y;
        e.CheckedComposition = new(e.Composition); e.Dirty = false;
        e.Attempts = 0; e.AvailableAt = DateTimeOffset.MinValue; Save();
    }

    public void Fail(CycleTarget target, string reason)
    {
        if (!_entries.TryGetValue(target.TraderKey, out var e) || e.Revision != target.Revision) return;
        e.Attempts++; e.Reason = reason;
        e.AvailableAt = _clock().AddMinutes(e.Attempts switch { 1 => 2, 2 => 5, _ => 15 }); Save();
    }

    public MarketCycleStatus Status(MarketCycleStatus state)
    {
        var now = _clock(); var active = _entries.Values.Where(e => e.Active).ToArray();
        var pending = active.Where(e => NeedsRead(e, now)).ToArray();
        return state with { Active = active.Length, Pending = pending.Length,
            Checked = active.Count(e => e.LastCheckedAt is not null && !NeedsRead(e, now)),
            Deferred = pending.Count(e => e.AvailableAt > now),
            Overdue = active.Count(e => e.LastCheckedAt is not null && e.LastCheckedAt.Value.AddHours(24) <= now),
            Queue = pending.OrderBy(e => e.AvailableAt).ThenBy(e => Due(e)).Take(150)
                .Select(e => new CycleQueueRow(e.Name, !e.HasPosition ? "Awaiting position" : e.AvailableAt > now ? "Deferred" : "Ready",
                    e.Dirty || e.Attempts > 0 ? e.Reason : "24-hour check",
                    e.LastCheckedAt is null ? "Never checked" : $"{(now - e.LastCheckedAt.Value).TotalHours:F1} h", e.Attempts)).ToArray() };
    }

    private static bool NeedsRead(CycleTraderState e, DateTimeOffset now) => e.Dirty || e.LastCheckedAt is null || e.LastCheckedAt.Value.AddHours(24) <= now;
    private static DateTimeOffset Due(CycleTraderState e) => e.Dirty ? e.ChangedAt : e.LastCheckedAt?.AddHours(24) ?? e.ChangedAt;
    private static double Distance(double x, double y, double a, double b) => Math.Sqrt((x-a)*(x-a)+(y-b)*(y-b));
    private static void Invalidate(CycleTraderState e, string reason, DateTimeOffset now)
    {
        e.Dirty = true; e.Reason = reason; e.Revision++; e.ChangedAt = now;
        e.Attempts = 0; e.AvailableAt = DateTimeOffset.MinValue;
    }
}
