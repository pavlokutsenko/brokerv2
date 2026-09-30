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
                var available=await ClaimCycleTargetsAsync(cycle,radar);
                var targets=runtime.OneTraderPerTurn ? available.Take(1).ToArray() : available;
                if(targets.Count==0)
                {
                    Log($"INFO {runtime.Profile.Name}: finite price pass complete · returning to center for next broker");
                    cycle.Phase="Return to center";
                    if(runtime.OneTraderPerTurn)cycle.TraderTurnComplete=true;
                    break;
                }
                runtime.Status=$"Reading prices on the move · {targets.Count} targets · continuous reader session";
                cycle.ActiveTraderKey=runtime.OneTraderPerTurn?targets[0].TraderKey:null;
                if(runtime.OneTraderPerTurn)MarketTraderStarted?.Invoke(runtime);
                try {await RunCycleRouteAsync(runtime,cycle,targets,false);}
                finally {cycle.ActiveTraderKey=null;}
                if(runtime.OneTraderPerTurn)
                {
                    cycle.TraderTurnComplete=true;
                    break;
                }
            }
        }
        finally {await EndRouteSessionAsync(cycle);}
    }

    private async Task RunPriceSectionWorkerAsync(ProfileRuntime runtime,CycleRun cycle,string output,string input)
    {
        cycle.RouteCommandFile??=Path.Combine(cycle.Folder,$"session-{Guid.NewGuid():N}.command.json");
        await WriteRouteCommandAsync(cycle.RouteCommandFile,new {id=Path.GetFileName(output),input,output,finish=false});
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
            await WriteRouteCommandAsync(cycle.RouteCommandFile!,new {id=Guid.NewGuid().ToString("N"),finish=true});
            // Native ownership must be released before broker role/client changes.
            await session;
        }
        catch(IOException error)
        {
            // A transient sharing violation is retried above. If the command
            // still cannot be published, the idle worker exits on its own
            // bounded timeout; do not hand the market to another PID first.
            try {await session.WaitAsync(TimeSpan.FromSeconds(30));}
            catch(TimeoutException) {throw new InvalidOperationException("Route session still owns the native lane",error);}
            throw;
        }
        finally
        {
            if(!session.IsCompleted)
            {
                // A locked command file cannot be treated as a released native lane.
                // The worker will time out and unwind; retain its task for recovery.
                Log($"WARNING {cycle.Scope}: route command still owns its native session");
            }
            else try
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
    private static async Task WriteRouteCommandAsync<T>(string path,T command)
    {
        for(var attempt=0;;attempt++)
        {
            try {DurableJsonFile.Write(path,command,keepBackup:false);return;}
            catch(IOException) when(attempt<19) {await Task.Delay(50);}
        }
    }
}
