using System.Diagnostics;
using System.Text.Json;
using PriceCheck.Collection;
using PriceCheck.Contracts;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class LocalNativeFailureTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static async Task Run()
    {
        await Scenario(true);await Scenario(false);
        Console.WriteLine("LOCAL_NATIVE_FAILURE_OK zero_native_requests no_false_shop_failures no_checked_results preserved_targets recoverable_bounded_retry");
    }
    private static async Task Scenario(bool fatal)
    {
        using var fixture=new LocalTestFixture();var runtime=LocalTestFixture.Runtime(fatal?410:411);
        var now=DateTimeOffset.UtcNow;
        var snapshot=new RadarSnapshot {ProcessId=runtime.ProcessId!.Value,WorldCharacterDataAvailable=true,LivePlayerPositionAvailable=true,
            IsInsideCenterZone=true,CenterZoneConfigured=true,PlayerX=0,PlayerY=0,CapturedAtUtc=now,
            Traders=Enumerable.Range(0,20).Select(i=>new RadarPoint(i+1,"FaultShop"+i,1,100+i*10,100,0,true,now)).ToArray()};
        var radar=new FakeRadar{CurrentSnapshot=snapshot};
        var worker=new PreRequestFailureWorker(fatal?"ProcessEvent command bridge is busy: trigger=1 status=0":"Bounded route transport failed safely");
        var module=new CollectionModule(radar,_=>true,worker,()=>{},new(()=>{},Path.Combine(fixture.Root,"status")),new NeverMarketTasks());
        var store=fixture.Install(module,runtime.Profile);
        await module.AttachAsync(runtime,CancellationToken.None);await module.RefreshAsync(runtime);await module.SetCollectionAsync(runtime,true);
        var cycle=LocalTestFixture.Cycle(module,runtime);cycle.GetType().GetProperty("Phase")!.SetValue(cycle,"Reading prices");
        store.BeginPass(0,0,new CycleRadarPool());
        await LocalTestFixture.Step(module,runtime,snapshot);
        Check(worker.TargetCount==20&&store.PendingOperations().All(o=>o.Kind!="price")&&runtime.Cycle.PassRead==0,"Native infrastructure failure must not fabricate exact results.");
        if(fatal)
        {
            Check(runtime.ClientFault is not null&&store.Keys.All(k=>store.ErrorFor(k) is null),"Unsafe command before any request must not blame twenty shops.");
            Check(store.NextTargets().Count==20,"Unsafe failure retains all unresolved targets for a new client.");
            await module.RefreshAsync(runtime);await module.RefreshAsync(runtime);
            Check(worker.Runs==1 && runtime.IsCollectionEnabled && runtime.Cycle.Phase=="Client recovery",
                "Refresh must retain collection intent and never retry an armed command or convert its marker into a user Stop.");
        }
        else
        {
            Check(runtime.ClientFault is null&&store.Keys.All(k=>store.ErrorFor(k) is not null)&&store.NextTargets().Count==20,"Recoverable route failure retains one bounded retry.");
            await LocalTestFixture.Step(module,runtime,snapshot);
            Check(store.NextTargets().Count==0&&runtime.IsCollectionEnabled,"Second recoverable failure remains unresolved and cannot loop forever.");
        }
        await module.DetachAsync(runtime);
    }
    private sealed class PreRequestFailureWorker(string reason):ICollectionWorker
    {
        public int Runs {get;private set;}
        public int TargetCount {get;private set;}
        public Task RunAsync(ClientSession session,string mode,string output,Action<ProcessStartInfo> configure)
        {
            Runs++;
            var start=new ProcessStartInfo();configure(start);
            using var command=JsonDocument.Parse(File.ReadAllText(start.ArgumentList[1]));
            var section=command.RootElement.TryGetProperty("input",out var sectionPath)
                ? sectionPath.GetString()! : start.ArgumentList[1];
            using var input=JsonDocument.Parse(File.ReadAllText(section));
            TargetCount=input.RootElement.GetProperty("targets").GetArrayLength();
            // No native request event, shop event or result file has been created.
            return Task.FromException(new InvalidOperationException(reason));
        }
    }
}
