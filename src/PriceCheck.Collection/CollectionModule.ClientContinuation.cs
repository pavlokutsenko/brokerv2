using PriceCheck.Collector.Models;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    public async Task SuspendForClientChangeAsync(ProfileRuntime runtime)
    {
        await DetachAsync(runtime,true);
        if(_cycles.TryGetValue(runtime.Profile.Id,out var cycle))
        {cycle.Bindings.BeginSession();cycle.RadarPool.BeginSession();}
        _cycles.Remove(runtime.Profile.Id);
        _localStoreSessions.Remove(runtime.Profile.Id);
        Log($"INFO {runtime.Profile.Name}: client changed · previous route cancelled · history and outbox retained");
    }
    // Each new client starts center, broker, reconciliation, then a newly planned route.
    private bool ResumeCycleAfterClientChange(ProfileRuntime runtime)=>false;
}
