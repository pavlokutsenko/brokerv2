using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Windows.Storage;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private static object[] RouteTargets(IReadOnlyList<CycleTarget> targets)
        =>targets.Select(t=>(object)new {traderKey=t.TraderKey,name=t.Name,x=t.X,y=t.Y,
            object_id=t.ObjectId,kiosk_type=t.KioskType,rebind=t.RebindOnRead,
            verification_revision=t.VerificationRevision??t.Revision.ToString(),local_revision=t.Revision}).ToArray();

    private static JsonElement? PreparedRoute(CycleRun cycle)
    {
        // A newer calculation may be running. The last atomically completed
        // geometry stays useful; the executor checks its map and target hashes.
        var file=cycle.NextSectionPlanFile;
        try {using var doc=JsonDocument.Parse(File.ReadAllText(file));return doc.RootElement.Clone();}
        catch(Exception e) when(e is IOException or JsonException){return null;}
    }

    private async Task PrepareFollowingRouteAsync(ProfileRuntime runtime,CycleRun cycle,
        IReadOnlyList<CycleTarget> current,string routePrefix,CancellationToken stop)
    {
        var planFile=routePrefix+".plan.json";
        try
        {
            // Wait while the current worker prepares; movement and streaming
            // remain in its one native ownership lane. This task uses no client.
            var requested=routePrefix+".prepare.input.json";
            var requestedOutput=routePrefix+".prepare.plan.json";
            var currentPrepared=false;
            while(!File.Exists(planFile))
            {
                if(!currentPrepared && File.Exists(requested) && cycle.BackgroundPlan is not {IsCompleted:false} && runtime.Session is {} active)
                {
                    currentPrepared=true;
                    cycle.BackgroundPlan=_worker.RunAsync(active,"market-plan",requestedOutput,p=>{
                        p.Environment["PRICECHECK_STOP_FILE"]=cycle.StopFile;
                        p.ArgumentList.Add("--input");p.ArgumentList.Add(requested);
                    });
                    try
                    {
                        await cycle.BackgroundPlan;
                        Log($"INFO {runtime.Profile.Name}: current route ready in background · native approach continued");
                    }
                    catch(Exception e){Log($"WARNING {runtime.Profile.Name}: current background route unavailable · {e.Message}");}
                }
                await Task.Delay(200,stop);
            }
            stop.ThrowIfCancellationRequested();
            using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(planFile,stop));
            var points=doc.RootElement.GetProperty("points");
            if(points.GetArrayLength()==0)return;
            var start=points[points.GetArrayLength()-1].EnumerateArray().Select(v=>v.GetDouble()).ToArray();
            var input=Path.Combine(cycle.Folder,"next-section.input.json");
            var output=cycle.NextSectionPlanFile;
            var excluded=current.Select(t=>t.TraderKey).ToHashSet();
            string? previous=null;
            while(!stop.IsCancellationRequested)
            {
                if(cycle.BackgroundPlan is not {IsCompleted:false})
                {
                    var targets=CycleRouteSections.Select(cycle.Store.NextTargets(10000),excluded);
                    var signature=JsonSerializer.Serialize(RouteTargets(targets));
                    if(targets.Count>0 && signature!=previous && runtime.Session is {} session && runtime.IsCollectionEnabled)
                    {
                        previous=signature;
                        DurableJsonFile.Write(input,new {city="Giran",planningStart=start,targets=RouteTargets(targets)},keepBackup:false);
                        cycle.BackgroundPlan=PlanAsync();
                        async Task PlanAsync()
                        {
                            try
                            {
                                await _worker.RunAsync(session,"market-plan",output,p=>{
                                    p.Environment["PRICECHECK_STOP_FILE"]=cycle.StopFile;
                                    p.ArgumentList.Add("--input");p.ArgumentList.Add(input);
                                });
                                Log($"INFO {runtime.Profile.Name}: background route ready · {targets.Count} targets · no native commands");
                            }
                            catch(Exception e){Log($"WARNING {runtime.Profile.Name}: background route unavailable · {e.Message} · foreground fallback");throw;}
                        }
                        _=cycle.BackgroundPlan.ContinueWith(t=>{_ = t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
                    }
                }
                await Task.Delay(1000,stop);
            }
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested){}
        catch(Exception e) when(e is IOException or JsonException){Log($"WARNING {runtime.Profile.Name}: next route preparation · {e.Message}");}
    }
}
