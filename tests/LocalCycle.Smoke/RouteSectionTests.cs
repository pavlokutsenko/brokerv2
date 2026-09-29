using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class RouteSectionTests
{
    public static void Run(string root)
    {
        var now=DateTimeOffset.UtcNow;
        var boundary=new CycleRadarPool();
        var center=new MarketZone(82710,148425,200);
        using var store=new LocalCycleStore(new(){Name="ROOM-ROUTE",ServerUrl="http://127.0.0.1:1"},root,()=>now);
        store.BeginSession("room-test");
        RadarPoint Point(string name,int id,double x,double y)=>new(id,name,1,x,y,0,true,now){StateObservedAtUtc=now};
        RadarSnapshot Frame(params RadarPoint[] points)=>new(){ProcessId=1,LivePlayerPositionAvailable=true,PlayerX=83930,PlayerY=148600,Traders=points,CapturedAtUtc=now};
        var temple=Enumerable.Range(0,65).Select(i=>Point("Temple"+i,i+1,83930+i%5*100,148010+i/5*90)).ToArray();
        store.Observe(Frame([..temple,Point("Outside",1000,83700,148600)]),boundary,center,now.AddSeconds(-1));
        store.BeginPass(83930,148600,boundary);
        var section=CycleRouteSections.Select(store.NextTargets(10000));
        if(section.Count!=65 || section.Any(t=>t.Name=="Outside"))throw new Exception("Temple split into repeated20-shop visits");
        var next=CycleRouteSections.Select(store.NextTargets(10000),section.Select(t=>t.TraderKey).ToHashSet());
        if(next.Count!=1 || next[0].Name!="Outside")throw new Exception("Background plan repeats current room");
        Console.WriteLine("PASS temple65 shops in one section; next plan excludes current room");
        using var priority=new LocalCycleStore(new(){Name="PRIORITY",ServerUrl="http://127.0.0.1:1"},root,()=>now);
        priority.BeginSession("priority-test");
        RadarSnapshot PriorityFrame(params RadarPoint[] points)=>new(){ProcessId=1,LivePlayerPositionAvailable=true,PlayerX=0,PlayerY=0,Traders=points,CapturedAtUtc=now};
        priority.Observe(PriorityFrame(Point("Ahead",1,100,0),Point("Far",2,200,0)),boundary,new(0,0,200),now.AddSeconds(-1));
        priority.BeginPass(0,0,boundary);now=now.AddSeconds(1);
        priority.Observe(PriorityFrame(Point("NewAhead",3,90,0),Point("NewBehind",4,-90,0)),boundary,new(0,0,200),now.AddSeconds(-1));
        var order=priority.NextTargets().Select(t=>t.Name).ToArray();
        if(order[0]!="NewAhead" || order[^1]!="NewBehind")throw new Exception("Forward new-shop priority regressed");
        Console.WriteLine("PASS new shops ahead receive priority; shops behind preserve forward direction");
    }
}
