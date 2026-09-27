using System.Text.Json;
using PriceCheck.Collection;
using PriceCheck.Collector.Services;

internal static class LocalCoordinatorTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static async Task Run()
    {
        using var fixture=new LocalTestFixture();
        var radar=new FakeRadar();var worker=new CycleReplayWorker();
        var module=new CollectionModule(radar,_=>true,worker,()=>{},new(()=>{},Path.Combine(fixture.Root,"status")),new NeverMarketTasks());
        var runtimes=Enumerable.Range(0,4).Select(i=>LocalTestFixture.Runtime(110+i)).ToArray();
        var stores=runtimes.Select(r=>fixture.Install(module,r.Profile)).ToArray();
        foreach(var runtime in runtimes)
        {
            radar.CurrentSnapshot=LocalTestFixture.Radar(runtime.ProcessId!.Value);
            await module.AttachAsync(runtime,CancellationToken.None);await module.RefreshAsync(runtime);
            await module.SetCollectionAsync(runtime,true);
            await LocalTestFixture.Step(module,runtime,radar.CurrentSnapshot);
            Check(LocalTestFixture.Phase(LocalTestFixture.Cycle(module,runtime))=="Broker inventory","Center immediately leads to broker.");
            await LocalTestFixture.Step(module,runtime,radar.CurrentSnapshot);
            Check(stores[Array.IndexOf(runtimes,runtime)].NextTargets().Count==2,"Broker creates a local two-shop route without server claims.");
        }
        var routes=runtimes.Select(r=>LocalTestFixture.Step(module,r,LocalTestFixture.Radar(r.ProcessId!.Value))).ToArray();
        await LocalTestFixture.Wait(()=>worker.PriceWaiters.Count==4,"All four independent collectors must run.");
        var stopA=module.SetCollectionAsync(runtimes[0],false);
        Check(File.Exists(worker.StopFiles[110])&&!File.Exists(worker.StopFiles[111]),"Stopping one profile must not signal another worker.");
        worker.PriceWaiters[110].SetResult();await routes[0];await stopA;
        Check(stores[0].ErrorFor("SHOP") is null&&stores[0].ErrorFor("LATER") is null,"Cancelling a route must not invent failed reads or retries for unattempted shops.");
        for(var i=1;i<4;i++)worker.PriceWaiters[110+i].SetResult();
        await Task.WhenAll(routes.Skip(1));
        for(var i=1;i<4;i++)
        {
            Check(runtimes[i].IsCollectionEnabled&&runtimes[i].ReaderAttached&&runtimes[i].Cycle.PassRead==2,"Each running profile counts its own durably read shops before HTTP acknowledgement.");
            var prices=stores[i].PendingOperations().Where(o=>o.Kind=="price").ToArray();
            Check(prices.Length==2&&stores[i].NextTargets().Count==0,"Each shop creates one separate durable upload and leaves the route while server is offline.");
            foreach(var price in prices){using var body=JsonDocument.Parse(price.Payload);Check(!body.RootElement.TryGetProperty("traders",out _),"No market price batch is created.");}
        }
        var previous=LocalTestFixture.Cycle(module,runtimes[1]);
        await module.SuspendForClientChangeAsync(runtimes[1]);runtimes[1].Session=new(211,DateTimeOffset.UtcNow);
        radar.CurrentSnapshot=LocalTestFixture.Radar(211);
        await module.AttachAsync(runtimes[1],CancellationToken.None);await module.RefreshAsync(runtimes[1]);
        await module.SetCollectionAsync(runtimes[1],true);
        Check(!ReferenceEquals(previous,LocalTestFixture.Cycle(module,runtimes[1]))&&runtimes[1].Cycle.PassRead==0,"New client cancels the old route and resets pass counters.");
        await LocalTestFixture.Step(module,runtimes[1],radar.CurrentSnapshot);await LocalTestFixture.Step(module,runtimes[1],radar.CurrentSnapshot);
        Check(stores[1].NextTargets().Count==0&&stores[1].PendingOperations().Count(o=>o.Kind=="price")==2,"New client retains offline own read history and does not reread fresh shops.");
        await LocalTestFixture.Step(module,runtimes[1],radar.CurrentSnapshot);
        Check(LocalTestFixture.Phase(LocalTestFixture.Cycle(module,runtimes[1]))=="Return to center","Finite exhausted route returns to center.");
        await LocalTestFixture.Step(module,runtimes[1],radar.CurrentSnapshot);
        Check(LocalTestFixture.Phase(LocalTestFixture.Cycle(module,runtimes[1]))=="Broker inventory","Return has no minimum interval before next broker.");
        foreach(var runtime in runtimes)await module.DetachAsync(runtime);
        Console.WriteLine("LOCAL_COORDINATOR_OK four_markets independent_stop individual_offline_prices new_client_new_route fresh_history finite_return immediate_broker no_server_queue");
    }
}
