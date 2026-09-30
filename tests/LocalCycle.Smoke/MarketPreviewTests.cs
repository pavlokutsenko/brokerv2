using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class MarketPreviewTests
{
    public static void Run(string root)
    {
        var now=DateTimeOffset.UtcNow;
        var profile=new CollectorProfile{Name="PREVIEW-"+Guid.NewGuid().ToString("N"),ServerUrl="http://127.0.0.1:1"};
        var boundary=new CycleRadarPool([[-1000,-1000],[1000,-1000],[1000,1000],[-1000,1000]]);
        using var store=new LocalCycleStore(profile,Path.Combine(root,"preview"),()=>now);
        var traders=new[]{("A",100.0,100.0),("B",200.0,200.0),("C",-100.0,-100.0)}
            .Select((value,index)=>new RadarPoint(index+1,value.Item1,1,value.Item2,value.Item3,0,true,now)
                {StateObservedAtUtc=now}).ToArray();
        var radar=new RadarSnapshot{ProcessId=123,LivePlayerPositionAvailable=true,PlayerX=0,PlayerY=0,
            Traders=traders,CapturedAtUtc=now};
        store.BeginSession("first");
        store.Observe(radar,boundary,new MarketZone(0,0,500),now.AddSeconds(-1));
        store.BeginPass(0,0,boundary);
        var active=store.NextTargets().First();
        var preview=store.PreviewNextTarget(300,0,boundary,active.TraderKey);
        if(preview is null)throw new Exception("The waiting account has no predicted market target.");
        store.FinishAttempt(active.TraderKey);
        store.ContinuePass(300,0,boundary);
        if(preview.TraderKey!=store.NextTargets().First().TraderKey || store.Status(new()).Pending!=2)
            throw new Exception("The waiting account preview differs from the shared pass after handoff.");
        Console.WriteLine("MARKET_PREVIEW_OK waiting_position shared_pass_next_target");
    }
}
