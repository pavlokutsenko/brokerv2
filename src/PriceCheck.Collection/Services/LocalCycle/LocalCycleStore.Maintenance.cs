namespace PriceCheck.Collector.Services;

public sealed partial class LocalCycleStore
{
    private DateTimeOffset _nextMaintenance=DateTimeOffset.MinValue;
    // Immutable full history is on the server. Local storage is a durable cache
    // and delivery queue: preserve every queued capture and current read anchor.
    public int MaintainStorage()
    {
        lock(_sync)
        {
            var now=_clock();
            if(now<_nextMaintenance)return 0;
            var anchors=_traders.Values.Where(t=>t.LastSnapshotId is not null)
                .Select(t=>t.LastSnapshotId!).ToHashSet(StringComparer.Ordinal);
            var candidates=_db.Query("""
                SELECT s.snapshot_id FROM snapshots s WHERE s.captured_at<?
                AND NOT EXISTS(SELECT 1 FROM outbox o WHERE o.operation_id=s.snapshot_id)
                AND EXISTS(SELECT 1 FROM snapshots n WHERE n.trader_key=s.trader_key
                    AND (n.captured_at>s.captured_at OR (n.captured_at=s.captured_at AND n.rowid>s.rowid)))
                ORDER BY s.captured_at LIMIT 2000
                """,now.AddDays(-7).ToUnixTimeMilliseconds())
                .Select(row=>row[0]!).Where(id=>!anchors.Contains(id)).ToArray();
            _db.Transaction(()=>{
                foreach(var id in candidates)_db.Command("DELETE FROM snapshots WHERE snapshot_id=?",id);
            });
            // Passive checkpoint never waits for other readers; reusable pages
            // remain available to subsequent captures without a blocking VACUUM.
            _db.Query("PRAGMA wal_checkpoint(PASSIVE)");
            _nextMaintenance=now.AddHours(1);
            return candidates.Length;
        }
    }
}
