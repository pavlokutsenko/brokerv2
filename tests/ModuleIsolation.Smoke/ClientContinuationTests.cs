using System.Collections;
using System.Reflection;
using System.Text.Json;
using PriceCheck.Collection;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class ClientContinuationTests
{
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static async Task Run()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var module=new CollectionModule(new FakeRadar(),_=>true,new PendingWorker(),()=>{},
            new(()=>{},Path.Combine(Path.GetTempPath(),"PriceCheck-continuation-"+Guid.NewGuid().ToString("N"))),new FakeMarketTasks());
        var runtime=new ProfileRuntime {Profile=new(){Name="Synthetic",City="Giran",
            CenterZonesByCity=new(){["Giran"]=new(){X=0,Y=0}}},Session=new(10,DateTimeOffset.UtcNow)};
        await module.AttachAsync(runtime,CancellationToken.None);
        await module.SetCollectionAsync(runtime,true);
        var cycles=(IDictionary)typeof(CollectionModule).GetField("_cycles",flags)!.GetValue(module)!;
        var cycle=cycles[runtime.Profile.Id]!;var type=cycle.GetType();
        type.GetProperty("Phase")!.SetValue(cycle,"Reading prices");
        type.GetProperty("BrokerBatch")!.SetValue(cycle,"previous-batch");
        var remaining=(HashSet<string>)type.GetProperty("RemainingVisitKeys")!.GetValue(cycle)!;
        remaining.Add("SHOP");
        var bindings=(CycleBindings)type.GetProperty("Bindings")!.GetValue(cycle)!;
        var pool=(CycleRadarPool)type.GetProperty("RadarPool")!.GetValue(cycle)!;
        bindings.AcceptExact(new("SHOP","Shop",12,1,100,100,0,"INITIAL_IMPORT",DateTimeOffset.UtcNow),DateTimeOffset.UtcNow);
        pool.Observe(new(){PlayerX=0,PlayerY=0,Traders=[new(13,"New",8,200,100,0,true,DateTimeOffset.UtcNow)]},new HashSet<string>(),false);
        pool.Capture("ALREADY_READ");
        runtime.Cycle=runtime.Cycle with {Cycles=3,PassRead=1};
        await module.SuspendForClientChangeAsync(runtime);
        Check(pool.Targets().Single().ObjectId==0 && pool.Counts().Captured==1,"Carry pool/counters, invalidate old PID objects.");
        runtime.Session=new(20,DateTimeOffset.UtcNow);
        await module.AttachAsync(runtime,CancellationToken.None);
        await module.SetCollectionAsync(runtime,true);
        Check(ReferenceEquals(cycle,cycles[runtime.Profile.Id]) && remaining.SetEquals(["SHOP"]) && runtime.Cycle.Cycles==3,
            "Character change preserves unfinished cycle and its counters.");
        Check((string)type.GetProperty("BrokerBatch")!.GetValue(cycle)! == "previous-batch" &&
              (string)type.GetProperty("ResumePhase")!.GetValue(cycle)! == "Reading prices","Resume prices without center or broker.");
        var job=new ServerPriceJob("1","SHOP","Shop",1,100,100,"cycle:fixture","INITIAL_IMPORT",DateTimeOffset.UtcNow,"lease");
        var unbound=bindings.Bind(job)!;
        Check(unbound.RebindOnRead && unbound.ObjectId==0,"Old ObjectID must never authorize new client's read.");
        var resolve=typeof(CollectionModule).GetMethod("ResolveContinuationTarget",BindingFlags.Static|BindingFlags.NonPublic)!;
        foreach(var (name,kiosk,x,accepted) in new[]{("Shop",1,100,true),("Other",1,100,false),("Shop",3,100,false),("Shop",1,120,false)})
        {
            using var document=JsonDocument.Parse(JsonSerializer.Serialize(new{precision="wire_int64",trader_key=name,
                trader=new{object_id=99,name,kiosk_type=kiosk,x,y=100}}));
            var result=(CycleTarget?)resolve.Invoke(null,[document.RootElement,unbound]);
            Check((result is not null)==accepted,"Fresh capture must match durable name, type and location.");
            if(result is not null) Check(result.ObjectId==99 && result.ServerJob==job,"Fresh identity retains server lease.");
        }
        bindings.Observe(new(){Traders=[new(100,"Shop",1,100,100,0,true,DateTimeOffset.UtcNow)]});
        Check(bindings.Bind(job)!.ObjectId==100 && !bindings.Bind(job)!.RebindOnRead,"Current radar rebinds new PID object.");
        await module.SetCollectionAsync(runtime,false);
        await module.SetCollectionAsync(runtime,true);
        Check(!ReferenceEquals(cycle,cycles[runtime.Profile.Id]),"Explicit stop/start begins a fresh pass.");
        await module.DetachAsync(runtime);
        Console.WriteLine("CLIENT_CONTINUATION_OK same_pass remaining_pool counters no_old_objects exact_rebind explicit_stop");
    }
}
