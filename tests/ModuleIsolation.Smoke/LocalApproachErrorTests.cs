using System.Diagnostics;
using System.Text.Json;
using PriceCheck.Collection;
using PriceCheck.Contracts;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class LocalApproachErrorTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static async Task Run()
    {
        await Scenario(false);await Scenario(true);
        Console.WriteLine("LOCAL_APPROACH_ERRORS_OK persisted_window_miss bounded_presence_reason limited_retry no_false_read_or_removal successful_capture_clears_prior_failure");
    }
    private static async Task Scenario(bool success)
    {
        using var fixture=new LocalTestFixture();var runtime=LocalTestFixture.Runtime(success?611:610);
        var radar=new FakeRadar{CurrentSnapshot=LocalTestFixture.Radar(runtime.ProcessId!.Value)};
        var module=new CollectionModule(radar,_=>true,new ApproachWorker(success),()=>{},new(()=>{},Path.Combine(fixture.Root,"status")),new NeverMarketTasks());
        var store=fixture.Install(module,runtime.Profile);
        await module.AttachAsync(runtime,CancellationToken.None);await module.RefreshAsync(runtime);await module.SetCollectionAsync(runtime,true);
        var cycle=LocalTestFixture.Cycle(module,runtime);cycle.GetType().GetProperty("Phase")!.SetValue(cycle,"Reading prices");
        store.BeginPass(0,0,new CycleRadarPool());await LocalTestFixture.Step(module,runtime,radar.CurrentSnapshot);
        Check(store.ErrorFor("LATER")=="Absent in bounded nearby scans; no market removal","Bounded absence reason must be persisted without market removal.");
        if(success)
        {
            Check(store.ErrorFor("SHOP") is null&&store.Target("SHOP") is null,"An old failure in the same result must not overwrite a successful current-generation read.");
            Check(store.PendingOperations().Count(o=>o.Kind=="price")==1,"Valid shop remains durably published independently of other failed approaches.");
        }
        else
        {
            Check(store.ErrorFor("SHOP")=="Passing window missed before shop request"&&store.NextTargets().Count==2,"Concrete missed-window reason must survive first bounded retry.");
            await LocalTestFixture.Step(module,runtime,radar.CurrentSnapshot);
            Check(store.ErrorFor("SHOP")=="Passing window missed before shop request"&&store.NextTargets().Count==0,"Final unresolved error must retain concrete reason after retry limit.");
            Check(store.PendingOperations().All(o=>o.Kind is not ("price" or "state")),"Failed/absent approaches cannot fabricate a successful read or closure event.");
        }
        await module.DetachAsync(runtime);
    }
    private sealed class ApproachWorker(bool success):ICollectionWorker
    {
        public Task RunAsync(ClientSession session,string mode,string output,Action<ProcessStartInfo> configure)
        {
            var start=new ProcessStartInfo();configure(start);
            using var input=JsonDocument.Parse(File.ReadAllText(start.ArgumentList[1]));
            var target=input.RootElement.GetProperty("targets").EnumerateArray().First(t=>t.GetProperty("traderKey").GetString()=="SHOP");
            var now=DateTimeOffset.UtcNow;
            var shop=new {trader_key="Shop",precision="wire_int64",side="sell",at=now,read_started_at=now,
                verification_revision=target.GetProperty("verification_revision").GetString(),
                trader=new{object_id=7,name="Shop",kiosk_type=1,x=100,y=100},rows=new[]{new{item_id=100L,item_object_id=900L,quantity=3L,enchant=7,price=9L,base_price=0L}}};
            File.WriteAllText(output,JsonSerializer.Serialize(new {reason="completed",shops=success?new[]{shop}:[],
                attempted=new[]{"SHOP"},failures=new[]{new{key="shop",reason="Passing window missed before shop request"}},temporarilyUnavailable=new[]{"later"}}));
            return Task.CompletedTask;
        }
    }
}
