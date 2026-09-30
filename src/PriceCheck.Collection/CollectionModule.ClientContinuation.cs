using PriceCheck.Collector.Models;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    public async Task SuspendForClientChangeAsync(ProfileRuntime runtime)
    {
        var canContinue=_cycles.TryGetValue(runtime.Profile.Id,out var current) &&
            (current.Phase is "Reading prices" or "Resume route" or "Client recovery" ||
             current.Phase=="Waiting for server" && current.ResumePhase=="Reading prices") &&
            current.Store.HasPendingPass;
        await DetachAsync(runtime,true);
        if(_cycles.TryGetValue(runtime.Profile.Id,out var cycle))
        {
            if(canContinue)
            {
                cycle.Bindings.BeginSession();cycle.RadarPool.BeginSession();
                cycle.ContinueAfterClientChange=true;
                cycle.TraderTurnComplete=false;
                cycle.Phase="Resume route";
                cycle.PreviousDestination=null;
                cycle.Next=DateTimeOffset.MinValue;
                cycle.BackgroundPlan=null;
                cycle.PlanGeneration=Guid.NewGuid().ToString("N");
                cycle.PriceStreamPrefix=null;
                cycle.PriceEventReader=null;
                cycle.PriceSpooled.Clear();
            }
            else _cycles.Remove(runtime.Profile.Id);
        }
        _localStoreSessions.Remove(runtime.Profile.Id);
        Log($"INFO {runtime.Profile.Name}: client changed · {(canContinue?"unfinished price pool retained":"previous route completed or not started")} · old object IDs invalidated");
    }
    private bool ResumeCycleAfterClientChange(ProfileRuntime runtime)
    {
        if(!_cycles.TryGetValue(runtime.Profile.Id,out var cycle) || !cycle.ContinueAfterClientChange)return false;
        if(cycle.Scope!=$"{runtime.Profile.Name}:{runtime.Profile.City}" ||
            cycle.Center!=GetCenterZone(runtime.Profile) || cycle.UploadProfile.ServerUrl!=runtime.Profile.ServerUrl ||
            !cycle.Store.HasPendingPass)
        { cycle.ContinueAfterClientChange=false;return false; }
        File.Delete(cycle.StopFile);
        cycle.Phase="Resume route";
        runtime.Cycle=runtime.Cycle with {Phase=cycle.Phase,Detail="Waiting for new character position to continue the pending price pool"};
        return true;
    }
}
