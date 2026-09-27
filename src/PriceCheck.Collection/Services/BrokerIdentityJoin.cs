using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public static class BrokerIdentityJoin
{
    public static RadarPoint[] Resolve(int pid,IReadOnlySet<long> wanted,RadarSnapshot before,
        RadarSnapshot after,IEnumerable<RadarPoint> native)
    {
        IEnumerable<RadarPoint> Current(RadarSnapshot snapshot) => snapshot.ProcessId==pid
            ? snapshot.Traders.Concat(snapshot.BrokerIdentities) : [];
        return Current(before).Concat(Current(after)).Concat(native)
            .Where(t=>t.ObjectId>0 && wanted.Contains(t.ObjectId) && !string.IsNullOrWhiteSpace(t.Name) &&
                t.Name.Length<=32 && double.IsFinite(t.X) && double.IsFinite(t.Y))
            .GroupBy(t=>t.ObjectId).Select(g=>g.Last()).ToArray();
    }
}
