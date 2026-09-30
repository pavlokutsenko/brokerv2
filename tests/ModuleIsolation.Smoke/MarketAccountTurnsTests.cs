using PriceCheck.Collection;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using System.Text.Json;

internal static class MarketAccountTurnsTests
{
    private static void Check(bool value,string message)
    {if(!value)throw new Exception(message);}

    public static async Task Run()
    {
        using var fixture=new LocalTestFixture();
        var radar=new FakeRadar();
        var worker=new PendingWorker();
        var module=new CollectionModule(radar,_=>true,worker,()=>{},
            new(()=>{},Path.Combine(fixture.Root,"outbox")),new NeverMarketTasks());
        var first=LocalTestFixture.Runtime(501);
        var second=LocalTestFixture.Runtime(502);
        second.Profile.Name=first.Profile.Name;
        second.Profile.ServerUrl=first.Profile.ServerUrl;
        var store=fixture.Install(module,first.Profile);
        await module.AttachAsync(first,CancellationToken.None);
        await module.AttachAsync(second,CancellationToken.None);
        Check(radar.Active.SetEquals([501,502]),"Two readers for one market retain distinct PIDs.");
        await module.SetCollectionAsync(first,true);
        var now=DateTimeOffset.UtcNow;
        store.BeginSession("501");
        store.Observe(LocalTestFixture.Radar(501),new CycleRadarPool(),new(0,0,500),now);
        store.BeginPass(0,0,new CycleRadarPool());
        Check(module.MarketStatus(first.Profile.Name,new()).Pending==2,
            "The market counter did not include the shared pending pass.");
        var cycle=LocalTestFixture.Cycle(module,first);
        cycle.GetType().GetProperty("Phase")!.SetValue(cycle,"Reading prices");
        cycle.GetType().GetProperty("ActiveTraderKey")!.SetValue(cycle,"SHOP");
        first.OneTraderPerTurn=second.OneTraderPerTurn=true;
        second.Radar=new(){ProcessId=502,LivePlayerPositionAvailable=true,PlayerX=0,PlayerY=0,
            CapturedAtUtc=now.AddMinutes(-5)};
        module.PrepareMarketTurn(first,second);
        await LocalTestFixture.Wait(()=>worker.Runs==1,"Waiting account did not start offline route planning.");
        module.PrepareMarketTurn(first,second);
        Check(worker.Runs==1,"The same waiting turn started duplicate planners.");
        worker.Complete.SetCanceled();
        var plan=cycle.GetType().GetProperty("UpcomingPlan")!.GetValue(cycle)!;
        await LocalTestFixture.Wait(()=>((Task)plan.GetType().GetProperty("Work")!.GetValue(plan)!).IsCompleted,
            "The cancelled planner did not release its slot.");
        module.PrepareMarketTurn(first,second);
        await LocalTestFixture.Wait(()=>worker.Runs==2,"A cancelled planner was treated as a usable route.");
        var folder=(string)cycle.GetType().GetProperty("Folder")!.GetValue(cycle)!;
        var account=second.Profile.Id.ToString("N");
        using(var input=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,$"next-section-{account}.input.json"))))
            Check(input.RootElement.GetProperty("targets")[0].GetProperty("traderKey").GetString()=="LATER",
                "The waiting account did not plan the next shared trader.");
        cycle.GetType().GetProperty("ActiveTraderKey")!.SetValue(cycle,null);
        cycle.GetType().GetProperty("TraderTurnComplete")!.SetValue(cycle,true);
        Check(module.MarketTurnComplete(first),"One completed trader releases the account turn.");
        await module.TransferMarketTurnAsync(first,second);
        Check(((string)cycle.GetType().GetProperty("NextSectionPlanFile")!.GetValue(cycle)!).EndsWith($"next-section-{account}.plan.json"),
            "The transferred reader did not select the waiting account's prepared route.");
        Check(!first.IsCollectionEnabled && second.IsCollectionEnabled && first.ReaderAttached && second.ReaderAttached,
            "The reader lease moves while both clients stay loaded.");
        Check(ReferenceEquals(cycle,LocalTestFixture.Cycle(module,second)) && store.HasPendingPass,
            "The shared pending pass follows the next account.");
        Check(module.MarketStatus(second.Profile.Name,new()).Pending==2,
            "The market counter changed when ownership moved to another PID.");
        radar.CurrentSnapshot=new(){ProcessId=502,WorldCharacterDataAvailable=true,LivePlayerPositionAvailable=false,
            CapturedAtUtc=now,Traders=[]};
        await module.RefreshAsync(second);
        Check(store.NextTargets().All(target=>target.ObjectId==0 && target.RebindOnRead),
            "Old PID object IDs cannot authorize the next account's reads.");
        await module.DetachAsync(first);
        await module.DetachAsync(second);
        Console.WriteLine("MARKET_ACCOUNT_TURNS_OK same_market_readers exclusive_turn shared_pass waiting_route fresh_binding");
    }
}
