using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public static class CenterRadarEvidence
{
    public static int CountShopTraders(IReadOnlyList<BrokerNativeStateObservation> states) => states
        .Where(s=>s.KioskType is 1 or 3 or 8)
        .Select(s=>CycleQueue.Key(s.Name))
        .Where(key=>key.Length>0).Distinct().Count();

    // Broker inventory can retain unnamed rows. Only a separately confirmed,
    // complete current actor observation can prove an absent historical shop.
    public static bool IsComplete(BrokerInventoryFile inventory, int pid, MarketZone center,
        DateTimeOffset started, DateTimeOffset now)
    {
        var proof=inventory.NativeRadar;
        var states=inventory.NativeStateObservations;
        return proof is { Complete:true } && inventory.BindingPid==pid && proof.ProcessId==pid &&
            proof.StartedAtUtc>=started && proof.ObservedAtUtc>=proof.StartedAtUtc &&
            proof.ObservedAtUtc<=now.AddSeconds(5) && now-proof.ObservedAtUtc<=TimeSpan.FromSeconds(5) &&
            proof.ActorCount>=proof.IdentityCount && proof.IdentityCount>0 && proof.IdentityCount==states.Count &&
            Inside(proof.CollectorX,proof.CollectorY,center) &&
            states.All(s=>s.Name.Trim().Length>0 && s.ObjectId>0 &&
                double.IsFinite(s.X) && double.IsFinite(s.Y) && s.ObservedAt==proof.ObservedAtUtc &&
                s.CollectorX==proof.CollectorX && s.CollectorY==proof.CollectorY) &&
            states.Select(s=>s.ObjectId).Distinct().Count()==states.Count &&
            states.Any(s=>s.KioskType is 1 or 3 or 8);
    }

    private static bool Inside(double x,double y,MarketZone center) =>
        double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(center.X) && double.IsFinite(center.Y) &&
        double.IsFinite(center.Radius) && center.Radius>0 && center.Radius<=500 &&
        Math.Pow(x-center.X,2)+Math.Pow(y-center.Y,2)<=center.Radius*center.Radius;
}
