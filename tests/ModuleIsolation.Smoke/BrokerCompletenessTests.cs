using System.Collections;
using System.Reflection;
using System.Text.Json;
using PriceCheck.Collection;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class BrokerCompletenessTests
{
    public static async Task Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"PriceCheck-broker-complete-"+Guid.NewGuid().ToString("N"));
        var radar=new FakeRadar {CurrentSnapshot=new() {IsInsideCenterZone=true,CenterZoneConfigured=true}};
        var worker=new CycleReplayWorker {UnboundBroker=true};
        var module=new CollectionModule(radar,_=>true,worker,()=>{},new ServerUploadOutbox(()=>{},folder),new FakeMarketTasks());
        var runtime=new ProfileRuntime {Profile=new() {Name="Synthetic",City="Giran",
            CenterZonesByCity=new() {["Giran"]=new() {X=0,Y=0}}},Session=new(10,DateTimeOffset.UtcNow)};
        await module.AttachAsync(runtime,CancellationToken.None);
        await module.SetCollectionAsync(runtime,true);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var cycles=(IDictionary)typeof(CollectionModule).GetField("_cycles",flags)!.GetValue(module)!;
        var cycle=cycles[runtime.Profile.Id]!;
        var phase=cycle.GetType().GetProperty("Phase")!;
        var step=typeof(CollectionModule).GetMethod("StepCycleAsync",flags)!;
        phase.SetValue(cycle,"Broker inventory");
        for(var i=1;i<=3;i++)
        {
            await (Task)step.Invoke(module,[runtime,cycle,radar.CurrentSnapshot])!;
            if(runtime.Cycle.Phase!=(i<3?"Broker inventory":"Stopped") || runtime.Cycle.Cycles!=0 || worker.PriceInputs.Count!=0)
                throw new Exception("Incomplete broker must retry in place, then stop; it cannot start a price pass.");
        }
        var partials=0;
        foreach(var path in Directory.GetFiles(folder,"*.ready"))
        {
            using var document=JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var body=document.RootElement.GetProperty("body");
            if(body.TryGetProperty("epochComplete",out var complete))
            {
                partials++;
                if(complete.GetBoolean() || body.GetProperty("completePresence").GetBoolean() ||
                    body.GetProperty("traders").EnumerateArray().Any(t=>t.GetProperty("inventoryComplete").GetBoolean()))
                    throw new Exception("Unbound broker must never authorize absence deletion.");
            }
        }
        if(partials!=3) throw new Exception("Every incomplete attempt must publish a safe partial observation.");
        worker.UnboundBroker=false;
        await module.SetCollectionAsync(runtime,true);
        cycle=cycles[runtime.Profile.Id]!;
        phase.SetValue(cycle,"Broker inventory");
        await (Task)step.Invoke(module,[runtime,cycle,radar.CurrentSnapshot])!;
        if(runtime.Cycle.Phase!="Reading prices" || runtime.Cycle.Cycles!=1)
            throw new Exception("A complete broker must start prices without an interval wait.");
        var closed=new CycleBindings();
        closed.Apply(new() {Rows=[new() {TraderName="Trader33",TraderObjectId=1338059371,StoreType=1,ItemId=100,Amount=1}]},
            new() {Traders=[new(1338059371,"Trader33",0,100,100,0,true,DateTimeOffset.UtcNow)]});
        var closedJob=new ServerPriceJob("3","TRADER33","Trader33",1,100,100,"cycle:fixture","INITIAL_IMPORT",DateTimeOffset.UtcNow,"lease");
        if(closed.Bind(closedJob,out var reason) is not null || reason is null || !reason.Contains("to 0"))
            throw new Exception("A closed identity can name a broker row but must never authorize a price approach.");
        await module.DetachAsync(runtime);
        Console.WriteLine("BROKER_COMPLETENESS_OK partial_retry no_price_pass no_deletion bounded_stop full_proceeds");
    }
}
