using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private async Task<bool> RunCycleBrokerAsync(ProfileRuntime runtime, CycleRun cycle, RadarSnapshot before)
    {
        if (!before.IsInsideCenterZone) throw new InvalidOperationException("Broker requires the configured center zone.");
        if (!cycle.RadarPool.CoversCenter(cycle.Center!.Value))
            throw new InvalidOperationException("Center standing zone does not cover the collection polygon with radar margin.");
        try { await cycle.Store.ImportActiveServerRosterAsync(cycle.RadarPool); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or
                                     InvalidDataException or JsonException)
        {
            Log($"WARNING {runtime.Profile.Name}: server roster unavailable; continuing local broker · {error.Message}");
        }
        var started = DateTimeOffset.UtcNow;
        Log($"INFO {runtime.Profile.Name}: broker started · PID {runtime.ProcessId} · epoch {started:O}");
        cycle.CollectingBrokerRadar=true;
        cycle.RadarPool.Reset();
        cycle.RadarPool.Observe(before,cycle.BrokerKeys,true);
        UpdateCycleRadarCounters(runtime,cycle);
        var output = Path.Combine(cycle.Folder, $"broker-{Guid.NewGuid():N}.json");
        cycle.ProgressFile = Path.ChangeExtension(output,".progress.json");
        await RunCycleWorkerAsync(runtime, cycle, "broker", output);
        var raw = JsonSerializer.Deserialize<BrokerInventoryFile>(await File.ReadAllTextAsync(output))
            ?? throw new InvalidDataException("Broker returned no data.");
        var after = _radarSessions.Snapshot(runtime.ProcessId!.Value, GetCenterZone(runtime.Profile), true)
            ?? throw new InvalidOperationException("Radar unavailable after broker pass.");
        foreach(var observation in raw.NativeStateObservations.Where(t=>t.KioskType==0))
            cycle.Store.ObserveClosed(new(observation.ObjectId,observation.Name,0,observation.X,observation.Y,0,true,observation.ObservedAt) {
                StateObservedAtUtc=observation.ObservedAt,StateObservedPlayerX=observation.CollectorX,StateObservedPlayerY=observation.CollectorY},
                cycle.Center!.Value,cycle.CenterEnteredAt,after.LivePlayerPositionAvailable);
        var bindings = raw.BindingPid == runtime.ProcessId ? raw.Bindings.Where(t=>double.IsFinite(t.X)&&double.IsFinite(t.Y))
            .Select(t=>new RadarPoint(t.ObjectId,t.Name,t.KioskType,t.X,t.Y,0,true,raw.CapturedAtUtc)).ToArray() : [];
        var wanted=raw.Rows.Select(r=>(long)r.TraderObjectId).ToHashSet();
        var positions = BrokerIdentityJoin.Resolve(runtime.ProcessId!.Value,wanted,before,after,bindings);
        var enriched = new RadarSnapshot { Traders=positions, PlayerX=after.PlayerX, PlayerY=after.PlayerY,
            IsInsideCenterZone=after.IsInsideCenterZone, CapturedAtUtc=after.CapturedAtUtc };
        var names = positions.ToDictionary(t=>(long)t.ObjectId);
        var rows = raw.Rows.Select(r => new BrokerInventoryRow { StoreType=r.StoreType, ItemId=r.ItemId,
            Amount=r.Amount, TraderObjectId=r.TraderObjectId,
            TraderName=names.TryGetValue(r.TraderObjectId,out var t) ? t.Name : "" }).ToArray();
        var nativeIds=bindings.Select(t=>(long)t.ObjectId).ToHashSet();
        var wantedIds=rows.Select(r=>r.TraderObjectId).Distinct().ToArray();
        await File.WriteAllTextAsync(Path.ChangeExtension(output,".identity-join.json"),JsonSerializer.Serialize(new {
            pid=runtime.ProcessId,started,at=DateTimeOffset.UtcNow,traders=wantedIds.Length,
            native=bindings.Length,beforePacket=before.Traders.Count,afterPacket=after.Traders.Count,
            beforeIdentities=before.BrokerIdentities.Count,afterIdentities=after.BrokerIdentities.Count,
            packetRecovered=wantedIds.Where(id=>!nativeIds.Contains(id) && names.ContainsKey(id)).Select(id=>names[id]),
            unbound=wantedIds.Where(id=>!names.ContainsKey(id)).ToArray() }));
        var stationary = after.IsInsideCenterZone &&
            cycle.Center == GetCenterZone(runtime.Profile) && runtime.IsCollectionEnabled && !File.Exists(cycle.StopFile) &&
            Math.Abs(before.PlayerX-after.PlayerX)<10 && Math.Abs(before.PlayerY-after.PlayerY)<10;
        var complete = raw.Complete && rows.All(r => r.TraderName.Length > 0) && stationary;
        var radarComplete = stationary && CenterRadarEvidence.IsComplete(raw,runtime.ProcessId!.Value,
            cycle.Center!.Value,started,DateTimeOffset.UtcNow);
        if(radarComplete)
        {
            runtime.ConfirmCenterRadarTraderCount(CenterRadarEvidence.CountShopTraders(raw.NativeStateObservations),
                raw.NativeRadar!.ObservedAtUtc);
            var live=new RadarSnapshot{CapturedAtUtc=raw.NativeRadar!.ObservedAtUtc,
                ProcessId=runtime.ProcessId.Value,WorldCharacterDataAvailable=true,LivePlayerPositionAvailable=true,
                IsInsideCenterZone=true,PlayerX=raw.NativeRadar.CollectorX,PlayerY=raw.NativeRadar.CollectorY,
                Traders=raw.NativeStateObservations.Where(t=>t.KioskType is 1 or 3 or 8)
                    .GroupBy(t=>CycleQueue.Key(t.Name)).Where(group=>group.Key.Length>0)
                    .Select(group=>group.MaxBy(t=>t.ObservedAt)!)
                    .Select(t=>new RadarPoint(t.ObjectId,t.Name,t.KioskType,t.X,t.Y,0,true,t.ObservedAt){StateObservedAtUtc=t.ObservedAt}).ToArray()};
            cycle.Store.Observe(live,cycle.RadarPool,cycle.Center.Value,cycle.CenterEnteredAt,publishNewPresence:true);
            cycle.Store.RecordMarketRadar(live);
        }
        var inventory = new BrokerInventoryFile { Complete=complete, StartedAtUtc=started,
            CapturedAtUtc=raw.CapturedAtUtc, ElapsedSeconds=raw.ElapsedSeconds, Summary=raw.Summary, Rows=rows };
        var reopened=cycle.Bindings.Apply(inventory,enriched);
        cycle.BrokerKeys.Clear();
        foreach(var group in rows.Where(r=>r.TraderName.Length>0).GroupBy(r=>CycleQueue.Key(r.TraderName)))
        {
            cycle.BrokerKeys.Add(group.Key);
        }
        cycle.RadarPool.Observe(after,cycle.BrokerKeys,true);
        cycle.CollectingBrokerRadar=false;
        cycle.RadarPool.BrokerCompleted(cycle.BrokerKeys);
        UpdateCycleRadarCounters(runtime,cycle);
        await File.WriteAllTextAsync(Path.ChangeExtension(output,".radar-pool.json"),
            JsonSerializer.Serialize(new {started,at=DateTimeOffset.UtcNow,targets=cycle.RadarPool.Targets()}));
        cycle.RemainingVisitKeys.Clear();
        cycle.ApproachReplans.Clear();cycle.UnresolvedApproaches.Clear();
        cycle.RemainingVisitKeys.UnionWith(rows.Where(r=>r.TraderName.Length>0).Select(r=>CycleQueue.Key(r.TraderName)));
        cycle.BrokerBatch=$"broker-{runtime.Profile.Id:N}-{Guid.NewGuid():N}";
        cycle.Store.ApplyBroker(inventory,enriched,cycle.BrokerBatch,cycle.RadarPool);
        var retired=cycle.Store.ReconcileCenterRadar(after,cycle.Center!.Value,started,cycle.BrokerBatch,radarComplete,raw.NativeStateObservations);
        Log($"INFO {runtime.Profile.Name}: center radar · complete={radarComplete} · native identities {raw.NativeStateObservations.Count} · retired {retired}");
        if(retired>0)Log($"INFO {runtime.Profile.Name}: center radar · {retired} historical traders retired · history retained");
        foreach(var observation in raw.NativeStateObservations.Where(t=>t.KioskType==0))
            cycle.Store.ObserveClosed(new(observation.ObjectId,observation.Name,0,observation.X,observation.Y,0,true,observation.ObservedAt) {
                StateObservedAtUtc=observation.ObservedAt,StateObservedPlayerX=observation.CollectorX,StateObservedPlayerY=observation.CollectorY},
                cycle.Center!.Value,cycle.CenterEnteredAt,after.LivePlayerPositionAvailable);
        cycle.ClaimRequest=Guid.NewGuid().ToString();
        runtime.Broker = new BrokerSnapshot { UniqueTraders=raw.Summary.UniqueTraders, ListingRows=raw.Summary.ListingRows,
            CapturedAtUtc=raw.CapturedAtUtc, ElapsedSeconds=raw.ElapsedSeconds };
        runtime.Cycle = runtime.Cycle with { Cycles=runtime.Cycle.Cycles+1, LastBrokerAt=raw.CapturedAtUtc };
        if (!complete)
        {
            var missing = rows.Where(r=>r.TraderName.Length==0).ToArray();
            runtime.Status = $"WARNING Broker incomplete · replies {raw.Summary.ItemResponses}/{raw.Summary.ItemRequests} · {missing.Select(r=>r.TraderObjectId).Distinct().Count()} unbound traders / {missing.Length} rows · safe data retained; continuing local route";
            Log($"WARNING {runtime.Profile.Name}: {runtime.Status}");
            foreach(var warning in raw.Warnings)Log($"WARNING {runtime.Profile.Name}: broker · {warning}");
            return radarComplete;
        }
        runtime.Status = $"Broker complete · {raw.Summary.UniqueTraders} traders · quantities updated";
        Log($"{runtime.Profile.Name}: {runtime.Status} · replies {raw.Summary.ItemResponses}/{raw.Summary.ItemRequests}");
        return radarComplete;
    }
}
