using System.Collections.Concurrent;
using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Windows.Storage;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private async Task RunContinuousPricesAsync(ProfileRuntime runtime,CycleRun cycle,RadarSnapshot radar)
    {
        try
        {
            while(runtime.IsCollectionEnabled && !File.Exists(cycle.StopFile))
            {
                await RefreshServerQueueAsync(runtime,cycle);
                var targets=await ClaimCycleTargetsAsync(cycle,radar);
                if(targets.Count==0)
                {
                    Log($"INFO {runtime.Profile.Name}: finite price pass complete · returning to center for next broker");
                    cycle.Phase="Return to center";break;
                }
                runtime.Status=$"Reading prices on the move · {targets.Count} targets · continuous reader session";
                await RunCycleRouteAsync(runtime,cycle,targets,false);
            }
        }
        finally {await EndRouteSessionAsync(cycle);}
    }

    private async Task RunPriceSectionWorkerAsync(ProfileRuntime runtime,CycleRun cycle,string output,string input)
    {
        cycle.RouteCommandFile??=Path.Combine(cycle.Folder,$"session-{Guid.NewGuid():N}.command.json");
        DurableJsonFile.Write(cycle.RouteCommandFile,new {id=Path.GetFileName(output),input,output,finish=false},keepBackup:false);
        if(cycle.RouteSession is null)
        {
            cycle.RouteSession=RunCycleWorkerAsync(runtime,cycle,"market-route-session",
                Path.ChangeExtension(cycle.RouteCommandFile,"result.json"),cycle.RouteCommandFile);
            Log($"INFO {runtime.Profile.Name}: continuous price session started · PID {runtime.ProcessId}");
        }
        while(!File.Exists(output))
        {
            if(cycle.RouteSession.IsCompleted)
            {
                await cycle.RouteSession;
                throw new InvalidDataException("Price session ended before section acknowledgement.");
            }
            await Task.Delay(25);
        }
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(output));
        if(doc.RootElement.TryGetProperty("error",out var error))
            throw new InvalidOperationException(error.GetString());
    }

    private async Task EndRouteSessionAsync(CycleRun cycle)
    {
        if(cycle.RouteSession is not {} session)return;
        try
        {
            DurableJsonFile.Write(cycle.RouteCommandFile!,new {id=Guid.NewGuid().ToString("N"),finish=true},keepBackup:false);
            // Native ownership must be released before broker role/client changes.
            await session;
        }
        finally
        {
            try
            {
                // A failed command read also closes/drains its reader. Keep
                // those completed captures even when the worker reports failure.
                if(cycle.PriceEventReader is {} tail && cycle.PriceStreamPrefix is {} prefix)
                    tail.VisitNewLines(line=>ProcessCycleEvent(cycle,prefix,
                        new Dictionary<string,CycleTarget>(),cycle.PriceSpooled,line),CancellationToken.None);
            }
            finally
            {
                cycle.RouteSession=null;cycle.RouteCommandFile=null;
                cycle.PriceStreamPrefix=null;cycle.PriceEventReader=null;cycle.PriceSpooled.Clear();
            }
        }
    }
}
