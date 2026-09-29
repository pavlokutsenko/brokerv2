using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class CenterRadarTests
{
    public static void Run(string root)
    {
        var now=DateTimeOffset.UtcNow;
        var boundary=new CycleRadarPool([[-500,-500],[500,-500],[500,500],[-500,500]]);
        var initialBoundary=new CycleRadarPool([[-1000,-1000],[1000,-1000],[1000,1000],[-1000,1000]]);
        var center=new MarketZone(0,0,500);
        RadarPoint Point(string name,int id,double x=100,bool visible=true)=>new(id,name,1,x,100,100,visible,now){StateObservedAtUtc=now};
        RadarSnapshot Frame(bool inside=true,params RadarPoint[] points)=>new(){ProcessId=123,LivePlayerPositionAvailable=true,
            WorldCharacterDataAvailable=true,IsInsideCenterZone=inside,PlayerX=inside?0:700,PlayerY=0,CapturedAtUtc=now,Traders=points};
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS center radar "+message);}
        using var store=new LocalCycleStore(new(){Name="CENTER-RADAR",ServerUrl="http://127.0.0.1:1"},root,()=>now);
        store.BeginSession("old");
        store.Observe(Frame(true,Point("Missing",1),Point("Kept",2),Point("Outside",3,700),Point("NewRead",4),Point("Native",5)),initialBoundary,center,now);
        store.BeginPass(0,0,initialBoundary);
        var read=store.Target("NEWREAD")!;
        now=now.AddMinutes(1);var started=now;
        store.BeginSession("new");
        now=now.AddSeconds(1);
        store.Observe(Frame(true,Point("Arrival",6)),boundary,center,started);
        store.Commit(read,new(){SnapshotId="center-concurrent-read",Side="sell",Precision="wire_int64",CapturedAtUtc=now,ReadStartedAtUtc=now,
            Rows=[new(){ItemId=57,ItemObjectId=7,Quantity=1,Price=100,EnchantLevel=0}]});
        now=now.AddSeconds(20);
        var frame=Frame(true,Point("Kept",2));
        Check(store.ReconcileCenterRadar(frame,center,started,"partial",false)==0,"partial scan retains history");
        Check(store.ReconcileCenterRadar(Frame(false,Point("Kept",2)),center,started,"roaming",true)==0,"roaming cannot retire traders");
        Check(store.ReconcileCenterRadar(Frame(),center,started,"empty",true)==0,"empty startup cache cannot retire traders");
        var native=new BrokerNativeStateObservation{Name="Native",KioskType=1,ObservedAt=now,CollectorX=0,CollectorY=0};
        Check(store.ReconcileCenterRadar(frame,center,started,"complete",true,[native])==1,"only old absent trader retires");
        var operation=store.PendingOperations().Single(o=>o.Kind=="state" && o.Payload.Contains("center_absent"));
        using(var document=JsonDocument.Parse(operation.Payload))
        {
            var body=document.RootElement;
            Check(body.GetProperty("traderKey").GetString()=="MISSING" && body.GetProperty("centerScanComplete").GetBoolean(),"durable proof belongs to missing identity");
            Check(body.GetProperty("kioskType").GetInt32()==1,"absence does not invent a kiosk zero packet");
        }
        Check(store.Target("MISSING") is null,"retired shop leaves price queue");
        Check(store.Target("KEPT") is not null && store.Target("ARRIVAL") is not null && store.Target("NATIVE") is not null,"visible and later arrival shops survive");
        Check(store.PendingOperations().Count(o=>o.Kind=="price")==1,"exact history preserved");
        Check(store.ReconcileCenterRadar(frame,center,started,"complete",true,[native])==0,"same observation is idempotent");
        now=now.AddSeconds(1);store.Observe(Frame(true,Point("Missing",99)),boundary,center,started);
        Check(store.Target("MISSING")?.Reason=="Shop reopened","fresh positive radar reopens retired identity with new verification");
        Console.WriteLine("CENTER_RADAR_OK partial roaming empty recent_read arrival native_presence history idempotency reopen");
    }
}
