using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    // Active local cycle deliberately never requests server assignments or leases.
    private Task RefreshServerQueueAsync(ProfileRuntime runtime,CycleRun cycle)
    {
        runtime.Cycle=cycle.Store.Status(runtime.Cycle) with {Phase=cycle.Phase,Detail=runtime.Status};
        return Task.CompletedTask;
    }
    private Task<IReadOnlyList<CycleTarget>> ClaimCycleTargetsAsync(CycleRun cycle,RadarSnapshot radar)
        =>Task.FromResult(CycleRouteSections.Select(cycle.Store.NextTargets(10000)));
}
