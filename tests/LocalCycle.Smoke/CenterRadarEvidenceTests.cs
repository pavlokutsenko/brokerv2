using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class CenterRadarEvidenceTests
{
    public static void Run()
    {
        var now=DateTimeOffset.UtcNow;
        var proof=new BrokerNativeRadar {ProcessId=7,Complete=true,StartedAtUtc=now.AddSeconds(-1),
            ObservedAtUtc=now,ActorCount=100,IdentityCount=1,CollectorX=0,CollectorY=0};
        BrokerInventoryFile Inventory(BrokerNativeRadar? p)=>new(){Complete=false,BindingPid=7,
            NativeRadar=p,NativeStateObservations=[new(){ObjectId=8,Name="Open",KioskType=1,
                X=100,Y=100,ObservedAt=now,CollectorX=p?.CollectorX??0,CollectorY=p?.CollectorY??0}],
            Rows=[new(){TraderObjectId=99,TraderName=""}]};
        bool Valid(BrokerNativeRadar? p)=>CenterRadarEvidence.IsComplete(Inventory(p),7,new(0,0,500),now.AddSeconds(-5),now);
        void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS center evidence "+label);}
        Check(Valid(proof),"unbound broker row does not invalidate a complete independent radar");
        Check(!Valid(null),"legacy worker cannot provide absence proof");
        Check(!Valid(proof with {Complete=false}),"partial native observation cannot remove shops");
        Check(!Valid(proof with {ProcessId=8}),"other process rejected");
        Check(!Valid(proof with {StartedAtUtc=now.AddMinutes(-1)}),"old observation rejected");
        Check(!Valid(proof with {ObservedAtUtc=now.AddSeconds(-6)}),"stale observation rejected");
        Check(!Valid(proof with {CollectorX=700}),"outside center rejected");
        Check(!Valid(proof with {IdentityCount=2}),"truncated identity list rejected");
        Check(!Valid(proof with {ActorCount=0}),"invalid actor count rejected");
        Check(Valid(proof with {CollectorX=201}),"observer at 201 is valid in legacy radius 500");
        Check(!CenterRadarEvidence.IsComplete(Inventory(proof with {CollectorX=201}),7,new(0,0,200),now.AddSeconds(-5),now),
            "observer outside reduced radius cannot retire shops");
        Check(CenterRadarEvidence.CountShopTraders([
            new(){Name="AB",KioskType=1},new(){Name="A\uFF22",KioskType=3},
            new(){Name="Package",KioskType=8},new(){Name="Closed",KioskType=0}])==2,
            "complete native shop count uses durable trader names and open kiosk types");
        var polygon=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine("maps","Giran","city.json")))
            .GetProperty("collectionZone").GetProperty("polygon").EnumerateArray()
            .Select(p=>p.EnumerateArray().Select(x=>x.GetDouble()).ToArray()).ToArray();
        var boundary=new CycleRadarPool(polygon);
        Check(!boundary.CoversCenter(new(82413.61851503256,148116.9785946493,500)),"old disk could lose radar coverage");
        Check(boundary.CoversCenter(new(82710,148425,200)),"calculated disk covers the complete expanded polygon");
        Console.WriteLine("CENTER_RADAR_EVIDENCE_OK");
    }
}
