using System.Text.Json;
using PriceCheck.Collection;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Collector.Runtime.Radar;

internal static class BrokerRadarPoolTests
{
    private static void Check(bool test,string message) { if(!test) throw new Exception(message); }
    public static async Task Run()
    {
        var captureFlags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
        using(var captureA=JsonDocument.Parse("""{"precision":"wire_int64","trader_key":"new","trader":{"object_id":12,"name":"New","kiosk_type":1,"x":100,"y":0},"rows":[],"side":"sell","at":"2026-09-26T12:00:00Z","capture_sequence":100,"reopened":true}"""))
        using(var captureB=JsonDocument.Parse("""{"precision":"wire_int64","trader_key":"new","trader":{"object_id":12,"name":"New","kiosk_type":1,"x":100,"y":0},"rows":[],"side":"sell","at":"2026-09-26T12:01:00Z","capture_sequence":101,"reopened":true}"""))
        {
            var captureTarget=new CycleTarget("NEW","New",12,1,100,0,0,"RADAR_NEW",DateTimeOffset.UtcNow);
            var method=typeof(CollectionModule).GetMethod("ExactCycleCapture",captureFlags)!;
            var a=(ShopCaptureFile)method.Invoke(null,[captureA.RootElement,captureTarget,"route-test"])!;
            var b=(ShopCaptureFile)method.Invoke(null,[captureB.RootElement,captureTarget,"route-test"])!;
            Check(a.SnapshotId!=b.SnapshotId,"Same-ID reopen captures need distinct durable snapshot IDs.");
            Check((bool)typeof(CollectionModule).GetMethod("ReopenedCapture",captureFlags)!.Invoke(null,[captureA.RootElement])!,
                "Reopened capture must bypass the completed old lease through radar admission.");
            var job=new ServerPriceJob("1","NEW","New",1,100,0,"cycle:fixture","REOPENED",DateTimeOffset.UtcNow,"lease-token");
            var leased=captureTarget with {ServerJob=job};
            var reopenedTarget=typeof(CollectionModule).GetMethod("ReopenedCycleTarget",captureFlags)!;
            var first=(CycleTarget)reopenedTarget.Invoke(null,[captureA.RootElement,leased,false])!;
            var later=(CycleTarget)reopenedTarget.Invoke(null,[captureB.RootElement,leased,true])!;
            Check(first.ServerJob==job && later.ServerJob is null,
                "Reopen before first read must acknowledge the owned lease; reopen after a prior upload must not reuse a completed lease.");
        }
        var store=new RadarEntityStore();
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(RadarEntityStore).GetMethod("SetObservationZone",flags)!.Invoke(store,[new MarketZone(0,0,500),null,true]);
        RadarSnapshot StoreSnapshot()=>(RadarSnapshot)typeof(RadarEntityStore).GetMethod("Snapshot",flags)!.Invoke(store,[1,null,null])!;
        store.Apply(new CharacterPacket(12,"New","",1,100,0,0));
        store.Apply(new DeletePacket(12));
        Check(StoreSnapshot().ClosedTraders.Count==0,"Delete/knownlist loss cannot export a closure signal.");
        store.Apply(new CharacterPacket(12,"New","",1,100,0,0));
        store.Apply(new CharacterPacket(12,"New","",0,110,0,0));
        var closedSnapshot=StoreSnapshot();
        Check(closedSnapshot.Traders.Count==0 && closedSnapshot.ClosedTraders.Single().KioskType==0 && closedSnapshot.ClosedTraders.Single().X==110,
            "Standing packet must export positive closure and current coordinates.");
        store.Apply(new CharacterPacket(12,"New","",8,120,0,0));
        Check(StoreSnapshot().ClosedTraders.Count==0 && StoreSnapshot().Traders.Single().KioskType==8,"Reopen clears the closure signal.");
        var pool=new CycleRadarPool([[-3100,-20],[3100,-20],[3100,20],[-3100,20]]);
        var known=new HashSet<string>{"SHOP"};
        var at=DateTimeOffset.UtcNow;
        RadarPoint Point(int id,string name,double x,double y=0)=>new(id,name,1,x,y,0,true,at);
        var carried=new CycleRadarPool();
        carried.Observe(new(){Traders=[Point(12,"Carried",100)]},new HashSet<string>(),false);
        carried.BeginSession();
        carried.NativeClosed("CARRIED",0,at.AddSeconds(1));
        Check(carried.Targets().Length==1,"An unbound identity cannot prove native closure.");
        carried.NativeClosed("CARRIED",99,at.AddSeconds(1));
        Check(carried.Targets().Length==0,"Validated current-worker closure must remove a carried destination.");
        carried.Observe(new(){Traders=[Point(99,"Carried",100)]},new HashSet<string>(),false);
        Check(carried.Targets().Length==0,"Old observations cannot reopen the carried closed shop.");
        carried.Observe(new(){Traders=[Point(99,"Carried",100) with {LastSeenAtUtc=at.AddSeconds(2)}]},new HashSet<string>(),false);
        Check(carried.Targets().Length==1,"A fresh positive reopen must be checked again.");
        var bindings=new CycleBindings();
        BrokerInventoryFile Inventory(int id,int type)=>new(){Complete=true,Rows=[new(){TraderName="New",TraderObjectId=id,StoreType=type,ItemId=1,Amount=1}]};
        bindings.Apply(Inventory(12,1),new(){Traders=[Point(12,"New",100)]});
        bindings.AcceptExact(new("NEW","New",13,3,100,0,0,"RADAR_NEW",at),at);
        Check(bindings.Apply(Inventory(13,3),new(){Traders=[Point(13,"New",100) with {KioskType=3}]}).Count==0,
            "Broker confirmation of already read radar generation must not schedule another reopen read.");
        Check(bindings.Apply(Inventory(14,3),new(){Traders=[Point(14,"New",100) with {KioskType=3}]}).Contains("NEW"),
            "An unread later generation still requires verification.");
        var nativePool=new CycleRadarPool();
        nativePool.Observe(new(){Traders=[Point(12,"New",100)]},new HashSet<string>(),false);
        nativePool.NativeClosed("NEW",13,at.AddSeconds(1));
        Check(nativePool.Counts().Pending==1,"Native closure cannot cancel a different generation.");
        nativePool.NativeClosed("NEW",12,at.AddSeconds(1));
        nativePool.Observe(new(){ClosedTraders=[Point(12,"New",100) with {KioskType=0,LastSeenAtUtc=at.AddMilliseconds(100)}]},new HashSet<string>(),false);
        nativePool.Observe(new(){Traders=[Point(12,"New",100)]},new HashSet<string>(),false);
        Check(nativePool.Counts().Pending==0 && nativePool.Counts().Captured==0,"Streamed native closure removes UI pool and blocks stale packets without claiming a read.");
        nativePool.Observe(new(){Traders=[Point(12,"New",100) with {LastSeenAtUtc=at.AddSeconds(2)}]},new HashSet<string>(),false);
        Check(nativePool.Counts().Pending==1,"A newer reopening remains eligible after streamed native closure.");
        var snapshot=new RadarSnapshot{Traders=[Point(1,"Shop",100),Point(2,"Late",2999),
            Point(3,"Far",3001),Point(4,"Outside",100,100)]};
        pool.Observe(snapshot,known,true);
        Check(pool.Targets().Length==2 && pool.Counts().Found==1,"Broker pool must filter radius/zone and distinguish known shops.");
        pool.Observe(new(){Traders=[Point(12,"Late",2500)]},known,true);
        pool.BrokerCompleted(known);
        Check(pool.Targets().Single().ObjectId==12,"Latest packet binding must survive broker and missing final frame.");
        // A UI tick can land during the async broker upload after its key filter.
        pool.Observe(snapshot,known,true);
        Check(pool.NewTargets(known).Length==1,"Late broker tick must not create work for known names.");
        pool.Observe(new(),known,false);
        Check(pool.Targets().Length==1,"Transition to prices must purge known names reinserted during broker upload.");
        pool.Capture("LATE");pool.Capture("LATE");pool.Observe(snapshot,known,false);
        Check(pool.Counts()==(1,0,1),"Successful capture must count once and cannot be requeued this pass.");
        pool.Reset();Check(pool.Counts()==(0,0,0),"New pass resets all local counters.");
        pool.Observe(new(){Traders=[Point(12,"Late",100)]},known,false);
        pool.Observe(new(){Traders=[Point(12,"Late",100) with {IsVisible=false}]},known,false);
        Check(pool.NewTargets(known).Length==1,"Visibility loss is not positive closure.");
        pool.Observe(new(){ClosedTraders=[Point(12,"Late",100) with {KioskType=0,LastSeenAtUtc=at.AddSeconds(1)}]},known,false);
        Check(pool.NewTargets(known).Length==0 && pool.Counts().Captured==0,"Explicit standing character removes pending visit without price acknowledgement.");
        pool.Review("LATE");
        pool.Observe(new(){Traders=[Point(12,"Late",100) with {LastSeenAtUtc=at.AddSeconds(2)}]},known,false);
        Check(pool.NewTargets(known).Length==1,"A newer positive reopen can re-enter this pass.");
        pool.Capture("LATE");known.Add("LATE");
        pool.Observe(new(){ClosedTraders=[Point(12,"Late",100) with {KioskType=0,LastSeenAtUtc=at.AddSeconds(3)}]},known,false);
        pool.Observe(new(){Traders=[Point(12,"Late",100) with {LastSeenAtUtc=at.AddSeconds(4)}]},known,false);
        Check(pool.NewTargets(known).Single().Name=="Late" && pool.IsReopened("LATE"),
            "Reopen after a successful read must bypass known-broker and reviewed suppression even with the same object ID.");

        var worker=new CycleReplayWorker{BrokerGate=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var radar=new FakeRadar{CurrentSnapshot=new(){IsInsideCenterZone=true,CenterZoneConfigured=true}};
        var outbox=Path.Combine(Path.GetTempPath(),"PriceCheck-cycle-tests",Guid.NewGuid().ToString("N"));
        var reader=new CollectionModule(radar,_=>true,worker,()=>{},new(()=>{},outbox),new FakeMarketTasks{NoJobs=true});
        var runtime=new ProfileRuntime{Profile=new(){CenterZonesByCity=new(){["Giran"]=new(){X=0,Y=0}}},Session=new(8002,at)};
        await reader.AttachAsync(runtime,CancellationToken.None);await reader.SetCollectionAsync(runtime,true);
        var deadline=DateTimeOffset.UtcNow.AddSeconds(8);
        while(worker.BrokerRuns==0 && DateTimeOffset.UtcNow<deadline) { await reader.RefreshAsync(runtime);await Task.Delay(30); }
        Check(worker.BrokerRuns==1,"Fake broker must be running before radar arrival.");
        radar.CurrentSnapshot=new(){IsInsideCenterZone=true,CenterZoneConfigured=true,Traders=[Point(12,"Late",2500),Point(7,"Shop",100)]};
        await reader.RefreshAsync(runtime);
        Check(runtime.Cycle.PassNewFound==2 && runtime.Cycle.RadarQueue.Any(t=>t.Name=="Late"),"UI must publish discoveries while broker is blocked.");
        // The final packet frame no longer contains Late; the accumulated pool must retain it.
        radar.CurrentSnapshot=new(){IsInsideCenterZone=true,CenterZoneConfigured=true};
        worker.BrokerGate.SetResult();
        deadline=DateTimeOffset.UtcNow.AddSeconds(5);
        while(!worker.PriceWaiters.ContainsKey(8002) && DateTimeOffset.UtcNow<deadline) { await reader.RefreshAsync(runtime);await Task.Delay(30); }
        Check(worker.PriceWaiters.ContainsKey(8002),"Radar-only route must start even with no server jobs.");
        using var input=JsonDocument.Parse(worker.PriceInputs[8002]);
        using var frame=JsonDocument.Parse(worker.InitialRadarFrames[8002]);
        Check(input.RootElement.GetProperty("targets").GetArrayLength()==0 && frame.RootElement.GetProperty("traders").GetArrayLength()==1,
            "Initial route radar frame contains only the new broker-time candidate.");
        var target=frame.RootElement.GetProperty("traders")[0];
        Check(target.GetProperty("object_id").GetInt32()==12 && target.GetProperty("observed_at").GetDateTimeOffset()==at,
            "Handoff must preserve original packet time and current PID binding.");
        Check(runtime.Cycle.PassNewFound==1 && runtime.Cycle.PassRadarPending==1,"Broker-known shops must leave the new-shop UI pool.");
        worker.PriceWaiters[8002].SetResult();
        deadline=DateTimeOffset.UtcNow.AddSeconds(3);
        while(runtime.Cycle.PassRadarPending>0 && DateTimeOffset.UtcNow<deadline) { await reader.RefreshAsync(runtime);await Task.Delay(30); }
        Check(runtime.Cycle.PassRadarPending==0 && runtime.Cycle.PassRead==0,"Reviewed absence must leave pool without claiming a price read.");
        await reader.DetachAsync(runtime);
        Console.WriteLine("BROKER_RADAR_POOL_OK packet_accumulation latest_binding zone radius counters radar_only_route reviewed_absence");
    }
}
