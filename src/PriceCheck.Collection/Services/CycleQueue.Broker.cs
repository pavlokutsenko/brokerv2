using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed partial class CycleQueue
{
    public void ApplyBroker(BrokerInventoryFile inventory, bool complete, DateTimeOffset startedAt)
    {
        var now = _clock(); var present = new HashSet<string>();
        foreach (var group in inventory.Rows.Where(r => !string.IsNullOrWhiteSpace(r.TraderName)).GroupBy(r => Key(r.TraderName)))
        {
            present.Add(group.Key); var first = group.First();
            if (!_entries.TryGetValue(group.Key, out var e))
                _entries[group.Key] = e = new() { Key = group.Key, Name = first.TraderName, ChangedAt = now };
            if (!e.Active) Invalidate(e, "Shop returned", now);
            else if (_boundThisSession.Contains(group.Key) &&
                (e.ObjectId!=first.TraderObjectId || e.KioskType!=first.StoreType)) Invalidate(e,"Shop reopened",now);
            e.Active = true; e.RemovedAt = null; e.ObjectId = first.TraderObjectId; e.KioskType = first.StoreType;
            var composition = group.GroupBy(r => $"{r.StoreType}:{r.ItemId}").ToDictionary(g => g.Key, g => g.Count());
            var added = composition.Any(p => p.Value > e.CheckedComposition.GetValueOrDefault(p.Key)
                || p.Value > e.Composition.GetValueOrDefault(p.Key));
            if (added && (!e.Dirty || !SameComposition(e.Composition, composition)))
                Invalidate(e, e.LastCheckedAt is null ? "New shop" : "New listing", now);
            e.Composition = composition;
        }
        if (complete)
        {
            foreach (var e in _entries.Values.Where(e => e.Active && !present.Contains(e.Key) && e.ChangedAt <= startedAt))
            { e.Active = false; e.RemovedAt = inventory.CapturedAtUtc; e.Revision++; }
        }
        Save();
    }

    private static bool SameComposition(Dictionary<string,int> a, Dictionary<string,int> b) =>
        a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);
}
