using System.Text.Json;
using PriceCheck.Collection;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class LocalRouteContinuationTests
{
    private static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }

    public static async Task Run()
    {
        using var fixture=new LocalTestFixture();
        var radar=new FakeRadar();var worker=new PendingWorker();
        var module=new CollectionModule(radar,_=>true,worker,()=>{},new(()=>{},Path.Combine(fixture.Root,"status")),new NeverMarketTasks());
        var runtime=LocalTestFixture.Runtime(401);
        var store=fixture.Install(module,runtime.Profile);
        var now=DateTimeOffset.UtcNow;
        RadarPoint Point(string name,int id,double x)=>new(id,name,1,x,0,0,true,now){StateObservedAtUtc=now};
        RadarSnapshot Frame(int pid,double x,bool live,params RadarPoint[] points)=>new()
        {
            ProcessId=pid,WorldCharacterDataAvailable=true,LivePlayerPositionAvailable=live,
            PlayerX=x,PlayerY=0,Traders=points,CapturedAtUtc=now
        };
        radar.CurrentSnapshot=Frame(401,0,true,Point("First",1,100),Point("Middle",2,300),Point("Last",3,500));
        await module.AttachAsync(runtime,CancellationToken.None);
        await module.SetCollectionAsync(runtime,true);
        store.BeginSession("401");
        store.Observe(radar.CurrentSnapshot,new CycleRadarPool(),new(0,0,500),now);
        store.BeginPass(0,0,new CycleRadarPool());
        var first=store.NextTargets().Single(t=>t.Name=="First");
        store.Commit(first,new ShopCaptureFile {SnapshotId="continuation-first",Precision="wire_int64",Side="sell",
            ReadStartedAtUtc=now,CapturedAtUtc=now,Rows=[new(){RowIndex=0,ItemId=1,ItemObjectId=10,Quantity=1,Price=100}]});
        var cycle=LocalTestFixture.Cycle(module,runtime);
        var oldPlan=(string)cycle.GetType().GetProperty("NextSectionPlanFile")!.GetValue(cycle)!;
        cycle.GetType().GetProperty("Phase")!.SetValue(cycle,"Reading prices");
        await module.SuspendForClientChangeAsync(runtime);
        runtime.Session=new(402,DateTimeOffset.UtcNow);
        radar.CurrentSnapshot=Frame(402,510,false);
        await module.AttachAsync(runtime,CancellationToken.None);
        await module.RefreshAsync(runtime);
        await module.SetCollectionAsync(runtime,true);
        await module.RefreshAsync(runtime);
        Check(ReferenceEquals(cycle,LocalTestFixture.Cycle(module,runtime)) && LocalTestFixture.Phase(cycle)=="Resume route",
            "Rotation retains the unfinished pass without starting a new center cycle.");
        Check((string)cycle.GetType().GetProperty("NextSectionPlanFile")!.GetValue(cycle)! != oldPlan,
            "New character cannot reuse an old-position prepared route.");
        var resumed=store.NextTargets();
        Check(store.Status(new()).PassRead==1 && resumed.Count==2 &&
              resumed.All(t=>t.ObjectId==0 && t.RebindOnRead),
            $"Completed reads and pending targets survive, but old client object IDs do not: " +
            $"read={store.Status(new()).PassRead} targets={string.Join(',',resumed.Select(t=>$"{t.Name}:{t.ObjectId}:{t.RebindOnRead}"))}.");
        await LocalTestFixture.Step(module,runtime,radar.CurrentSnapshot);
        Check(LocalTestFixture.Phase(cycle)=="Resume route","Route waits for a live new-character position.");
        radar.CurrentSnapshot=Frame(402,510,true);
        await LocalTestFixture.Step(module,runtime,radar.CurrentSnapshot);
        Check(LocalTestFixture.Phase(cycle)=="Reading prices" &&
              store.NextTargets().Select(t=>t.Name).SequenceEqual(["Last","Middle"]) && worker.Runs==0,
            "Pending pool replans from the new character, without center or broker work.");
        await module.SetCollectionAsync(runtime,false);
        await module.SetCollectionAsync(runtime,true);
        Check(!ReferenceEquals(cycle,LocalTestFixture.Cycle(module,runtime)) && !store.HasPendingPass,
            "Manual stop and start still begins a fresh pass.");
        await module.DetachAsync(runtime);
        Console.WriteLine("LOCAL_ROUTE_CONTINUATION_OK pending_pool fresh_identity new_origin no_center manual_restart");
    }
}
