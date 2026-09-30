using System.Reflection;
using System.Text.Json;
using PriceCheck.Collection;

internal static class LocalBrokerWarningTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static async Task Run()
    {
        using var fixture=new LocalTestFixture();var runtime=LocalTestFixture.Runtime(310);
        var radar=new FakeRadar{CurrentSnapshot=LocalTestFixture.Radar(310)};
        var worker=new CycleReplayWorker{IncompleteBroker=true};
        var module=new CollectionModule(radar,_=>true,worker,()=>{},new(()=>{},Path.Combine(fixture.Root,"status")),new NeverMarketTasks());
        var store=fixture.Install(module,runtime.Profile);var logs=new List<string>();module.Message+=logs.Add;
        await module.AttachAsync(runtime,CancellationToken.None);await module.RefreshAsync(runtime);await module.SetCollectionAsync(runtime,true);
        await LocalTestFixture.Step(module,runtime,radar.CurrentSnapshot);await LocalTestFixture.Step(module,runtime,radar.CurrentSnapshot);
        Check(worker.BrokerRuns==1&&LocalTestFixture.Phase(LocalTestFixture.Cycle(module,runtime))=="Reading prices","Incomplete safe broker continues to local route without another attempt.");
        Check(store.NextTargets().Count==2&&runtime.IsCollectionEnabled,"Partial quantities do not stop available price targets.");
        Check(logs.Any(l=>l.Contains("WARNING")&&l.Contains("replies 1/2"))&&logs.Any(l=>l.Contains("omitted one response")),"Broker warning records concrete counts and native reason.");
        var broker=store.PendingOperations().Where(o=>o.Kind=="broker").ToArray();
        Check(broker.Length==2,"Partial trustworthy broker data is stored per trader.");
        foreach(var item in broker){using var body=JsonDocument.Parse(item.Payload);Check(!body.RootElement.GetProperty("compositionComplete").GetBoolean(),"Incomplete epoch cannot authorize composition clearing.");}
        var cycle=LocalTestFixture.Cycle(module,runtime);cycle.GetType().GetProperty("Phase")!.SetValue(cycle,"Broker inventory");
        typeof(CollectionModule).GetMethod("RecordCycleFailure",LocalTestFixture.Hidden)!.Invoke(module,[runtime,cycle,new InvalidOperationException("Broker native cleanup failed; route must not start")]);
        Check(runtime.ClientFault is not null&&File.Exists((string)cycle.GetType().GetProperty("StopFile")!.GetValue(cycle)!)&&LocalTestFixture.Phase(cycle)=="Client recovery","Unsafe pending native command requires owned-client recovery rather than route continuation.");
        await module.DetachAsync(runtime);
        Console.WriteLine("LOCAL_BROKER_WARNING_OK no_local_retries partial_payloads warning_counts available_route unsafe_native_recovery");
    }
}
