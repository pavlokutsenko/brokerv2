using PriceCheck.Collector.Models;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private void RetryCycleApproach(CycleRun cycle,CycleTarget target,
        Dictionary<string,MarketTaskRelease> releases,string cause)
    {
        // Returning an unread lease is not a trader departure. The server keeps
        // the job ready; a local execution retry never imposes a queue deadline.
        releases[target.TraderKey]=new(target.ServerJob!);
        var attempts=cycle.ApproachReplans.GetValueOrDefault(target.TraderKey)+1;
        cycle.ApproachReplans[target.TraderKey]=attempts;
        Log($"WARNING {cycle.UploadProfile.Name}: failed approach {attempts}/2 for {target.Name} — {cause}");
        if(attempts>=2)
        {
            cycle.RemainingVisitKeys.Remove(target.TraderKey);
            cycle.UnresolvedApproaches.Add(target.TraderKey);
        }
    }
}
