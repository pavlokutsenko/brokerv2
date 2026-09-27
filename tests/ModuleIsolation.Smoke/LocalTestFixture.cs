using System.Collections;
using System.Reflection;
using PriceCheck.Collection;
using PriceCheck.Contracts;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal sealed class LocalTestFixture : IDisposable
{
    internal const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    public string Root { get; }=Path.Combine(Path.GetTempPath(),"PriceCheck-module-local-"+Guid.NewGuid().ToString("N"));
    private readonly List<LocalCycleStore> _stores=[];
    public LocalCycleStore Install(CollectionModule module,CollectorProfile profile)
    {
        var store=new LocalCycleStore(profile,Root);_stores.Add(store);
        ((IDictionary)typeof(CollectionModule).GetField("_marketStores",Hidden)!.GetValue(module)!)[CycleQueue.Key(profile.Name)]=store;
        return store;
    }
    public static object Cycle(CollectionModule module,ProfileRuntime runtime)=>
        ((IDictionary)typeof(CollectionModule).GetField("_cycles",Hidden)!.GetValue(module)!)[runtime.Profile.Id]!;
    public static string Phase(object cycle)=>(string)cycle.GetType().GetProperty("Phase")!.GetValue(cycle)!;
    public static Task Step(CollectionModule module,ProfileRuntime runtime,RadarSnapshot radar)=>
        (Task)typeof(CollectionModule).GetMethod("StepCycleAsync",Hidden)!.Invoke(module,[runtime,Cycle(module,runtime),radar])!;
    public static ProfileRuntime Runtime(int pid)=>new(){Profile=new(){Name="Synthetic-"+Guid.NewGuid().ToString("N"),City="Giran",
        ServerUrl="http://127.0.0.1:1",CenterZonesByCity=new(){["Giran"]=new(){X=0,Y=0}}},Session=new(pid,DateTimeOffset.UtcNow)};
    public static RadarSnapshot Radar(int pid)=>new(){ProcessId=pid,WorldCharacterDataAvailable=true,LivePlayerPositionAvailable=true,
        IsInsideCenterZone=true,CenterZoneConfigured=true,PlayerX=0,PlayerY=0,CapturedAtUtc=DateTimeOffset.UtcNow,
        Traders=[new(7,"Shop",1,100,100,0,true,DateTimeOffset.UtcNow),new(8,"Later",1,200,200,0,true,DateTimeOffset.UtcNow)]};
    public static async Task Wait(Func<bool> ready,string message)
    {for(var i=0;i<200&&!ready();i++)await Task.Delay(10);if(!ready())throw new Exception(message);}
    public void Dispose(){foreach(var store in _stores)store.Dispose();}
}

// Any contact with the old routing API is a failure, including a harmless read.
internal sealed class NeverMarketTasks : IMarketTaskClient
{
    private static Exception Legacy()=>new Exception("Local coordinator called legacy server queue API");
    public Task<MarketCycleStatus> StateAsync(CollectorProfile p)=>throw Legacy();
    public Task<MarketTaskClaim> ClaimAsync(CollectorProfile p,string worker,string batch,string request,IReadOnlyList<string> keys,double x,double y)=>throw Legacy();
    public Task<IReadOnlyList<string>> RenewAsync(CollectorProfile p,string worker,IReadOnlyList<ServerPriceJob> jobs)=>throw Legacy();
    public Task ReleaseAsync(CollectorProfile p,string worker,IReadOnlyList<MarketTaskRelease> jobs)=>throw Legacy();
}
