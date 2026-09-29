using System.Collections.Concurrent;
using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Windows.Storage;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private async Task RunCycleRouteAsync(ProfileRuntime runtime,CycleRun cycle,IReadOnlyList<CycleTarget> targets,bool center)
    {
        var spooled=center?new ConcurrentDictionary<string,byte>():cycle.PriceSpooled;
        foreach(var target in targets)spooled.TryRemove(target.TraderKey,out _);
        var unsafeFailure=false;
        try {await RunCycleRouteCoreAsync(runtime,cycle,targets,center,spooled);}
        catch(Exception error){unsafeFailure=IsUnsafeNativeFailure(error);throw;}
        finally
        {
            // Cancelled passes are discarded on stop/rotation. Their remaining
            // targets are unresolved, but cancellation is not a failed read.
            if(!center && !unsafeFailure && runtime.IsCollectionEnabled && cycle.Phase!="Stopped" && !File.Exists(cycle.StopFile))
                foreach(var target in targets.Where(t=>!spooled.ContainsKey(t.TraderKey)))
                cycle.Store.FinishAttempt(target.TraderKey,cycle.Store.ErrorFor(target.TraderKey)??"No complete valid shop capture from route");
        }
    }
    private async Task RunCycleRouteCoreAsync(ProfileRuntime runtime,CycleRun cycle,IReadOnlyList<CycleTarget> targets,
        bool center,ConcurrentDictionary<string,byte> spooled)
    {
        var prefix=Path.Combine(cycle.Folder,$"route-{Guid.NewGuid():N}");
        var input=prefix+".input.json";var output=prefix+".json";
        cycle.ProgressFile=prefix+".progress.json";
        if(!center)
        {
            cycle.PriceStreamPrefix??=prefix;
            cycle.RadarFile=cycle.PriceStreamPrefix+".radar.json";cycle.NextRadarWrite=default;
            if(runtime.Radar is {} radar)WriteCycleRadarTargets(cycle,radar);
        }
        DurableJsonFile.Write(input,new {city="Giran",market=runtime.Profile.Name,profileId=runtime.Profile.Id,localSection=true,mode=center?"center":"prices",center=new[]{cycle.Center!.Value.X,cycle.Center.Value.Y},centerRadius=cycle.Center.Value.Radius,
            previousDestination=cycle.PreviousDestination,
            duration=center?100:600,
            traderPauseSeconds=runtime.Profile.TraderPauseSeconds,
            radarFile=center?null:cycle.RadarFile,
            preparationInput=prefix+".prepare.input.json",preparationOutput=prefix+".prepare.plan.json",
            shopPrefix=cycle.PriceStreamPrefix,brokerKeys=cycle.BrokerKeys.ToArray(),targets=RouteTargets(targets),preparedPlan=center?null:PreparedRoute(cycle)},keepBackup:false);
        Log($"INFO {runtime.Profile.Name}: {(center?"return to center":"route section")} · {targets.Count} targets · PID {runtime.ProcessId}");
        Exception? failure=null;
        using var stop=new CancellationTokenSource();
        var byKey=targets.ToDictionary(t=>t.TraderKey);
        var streamPrefix=center?prefix:cycle.PriceStreamPrefix!;
        var stream=center?Task.CompletedTask:StreamCyclePricesAsync(cycle,streamPrefix,byKey,spooled,stop.Token);
        var preparation=center?Task.CompletedTask:PrepareFollowingRouteAsync(runtime,cycle,targets,prefix,stop.Token);
        try
        {
            if(center)await RunCycleWorkerAsync(runtime,cycle,"market-route",output,input);
            else await RunPriceSectionWorkerAsync(runtime,cycle,output,input);
        }
        catch(Exception e){failure=e;}
        finally
        {
            cycle.RadarFile=null;stop.Cancel();
            await preparation;
            File.Delete(prefix+".prepare.input.json");File.Delete(prefix+".prepare.plan.json");
            try{await stream;}catch(OperationCanceledException) when(stop.IsCancellationRequested){}
            catch(Exception e){Log($"ERROR {runtime.Profile.Name}: live snapshot persistence · {e}");failure??=e;}
        }
        if(!File.Exists(output))throw failure??new InvalidDataException("Route returned no result.");
        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(output));var result=document.RootElement;
        var reason=result.GetProperty("reason").GetString();
        if(reason is "cancelled" or "keyboard_interrupt" or "target_changed_externally" or "position_jump")
        {
            runtime.IsCollectionEnabled=runtime.Profile.CollectionEnabled=false;RequestCycleStop(runtime);cycle.Phase="Stopped";
            runtime.Cycle=runtime.Cycle with{Phase="Stopped",Detail=$"Route stopped: {reason}"};
        }
        if(center)
        {
            if(reason!="completed")
            {
                var current=runtime.ProcessId is int pid?_radarSessions.Snapshot(pid,cycle.Center,true):null;
                if(failure is not null || !runtime.IsCollectionEnabled || File.Exists(cycle.StopFile) ||
                    !TryAcceptCenterEndpoint(result,reason,cycle.Center!.Value,current,runtime.ProcessId!.Value,
                        DateTimeOffset.UtcNow,out var endpoint))
                    throw failure??new InvalidOperationException($"Return to center: {reason}");
                cycle.PreviousDestination=endpoint;
                var distance=Math.Sqrt(Math.Pow(endpoint[0]-cycle.Center.Value.X,2)+Math.Pow(endpoint[1]-cycle.Center.Value.Y,2));
                Log($"WARNING {runtime.Profile.Name}: center route {reason} · actual endpoint {distance:F0} from saved center · fresh radar confirms safe arrival");
            }
            else cycle.PreviousDestination=result.GetProperty("destination").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
            Log($"INFO {runtime.Profile.Name}: arrived in saved center zone · PID {runtime.ProcessId} · next broker immediately");
        }
        else
        {
            foreach(var shop in result.GetProperty("shops").EnumerateArray())
            {
                var key=CycleQueue.Key(shop.GetProperty("trader_key").GetString()!);
                var target=byKey.GetValueOrDefault(key)??ProvisionalCycleTarget(shop);
                if(target is null)continue;
                target=ResolveContinuationTarget(shop,target);if(target is null)continue;
                var capture=ExactCycleCapture(shop,target,streamPrefix);
                if(capture is null || spooled.ContainsKey(capture.SnapshotId))continue;
                CommitLocalCapture(cycle,target,capture,shop);spooled.TryAdd(capture.SnapshotId,0);spooled.TryAdd(key,0);
            }
            if(result.TryGetProperty("failures",out var failures))foreach(var fail in failures.EnumerateArray())
            {
                var key=CycleQueue.Key(fail.GetProperty("key").GetString()!);
                if(!spooled.ContainsKey(key)||cycle.Store.Target(key) is not null)
                    cycle.Store.RecordError(key,fail.GetProperty("reason").GetString()??"Native route reported a failed approach");
            }
            if(result.TryGetProperty("temporarilyUnavailable",out var unavailable))foreach(var value in unavailable.EnumerateArray())
            {
                var key=CycleQueue.Key(value.GetString()!);
                if(!spooled.ContainsKey(key)||cycle.Store.Target(key) is not null)
                    cycle.Store.RecordError(key,"Absent in bounded nearby scans; no market removal");
            }
        }
        UpdateCycleRadarCounters(runtime,cycle);
        if(failure is not null)throw failure;
    }

}
