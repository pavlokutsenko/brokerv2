using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class ServerRosterTests
{
    public static void Run(string root)
    {
        var now=DateTimeOffset.UtcNow;
        using var store=new LocalCycleStore(new(){Name="ROSTER",ServerUrl="http://127.0.0.1:1"},root,()=>now);
        store.BeginSession("roster");
        var boundary=new CycleRadarPool([[-1000,-1000],[1000,-1000],[1000,1000],[-1000,1000]]);
        var center=new MarketZone(0,0,200);
        using var data=JsonDocument.Parse(JsonSerializer.Serialize(new {activeRosterComplete=true,traders=new[]{
            new {traderKey="Legacy",displayName="Legacy",worldX=(double?)100,worldY=(double?)100,kioskType=1,isActive=true,lastSeenAtUtc=now.AddDays(-5)},
            new {traderKey="Outside",displayName="Outside",worldX=(double?)2000,worldY=(double?)100,kioskType=1,isActive=true,lastSeenAtUtc=now.AddDays(-5)},
            new {traderKey="Unknown",displayName="Unknown",worldX=(double?)null,worldY=(double?)null,kioskType=1,isActive=true,lastSeenAtUtc=now.AddDays(-5)}}}));
        if(store.ImportActiveServerRoster(data.RootElement,boundary)!=1 || store.PendingUploads!=0)
            throw new Exception("Roster import must not invent live presence, prices or closure");
        if(store.ImportActiveServerRoster(data.RootElement,boundary)!=0)throw new Exception("Roster import must be idempotent");
        using var partial=JsonDocument.Parse("{\"activeRosterComplete\":false,\"traders\":[]}");
        try{store.ImportActiveServerRoster(partial.RootElement,boundary);throw new Exception("Partial roster accepted");}
        catch(InvalidDataException){}
        var radar=new RadarSnapshot{CapturedAtUtc=now,WorldCharacterDataAvailable=true,LivePlayerPositionAvailable=true,
            IsInsideCenterZone=true,PlayerX=0,PlayerY=0,Traders=[new(7,"Present",1,200,200,0,true,now)]};
        store.Observe(radar,boundary,center,now.AddSeconds(-1),publishNewPresence:true);
        var presence=store.PendingOperations().Single(o=>o.Kind=="state");
        using(var payload=JsonDocument.Parse(presence.Payload))
            if(payload.RootElement.GetProperty("type").GetString()!="reopened" ||
                payload.RootElement.GetProperty("traderKey").GetString()!="PRESENT" ||
                payload.RootElement.TryGetProperty("rows",out _))throw new Exception("Full native positive presence must publish without a price");
        if(store.ReconcileCenterRadar(radar,center,now.AddSeconds(-1),"roster-full",true)!=1 ||
            !store.PendingOperations().Any(o=>o.Kind=="state" && o.Payload.Contains("LEGACY")))
            throw new Exception("Complete center radar must retire a previously unknown server-only legacy shop");
        // The local confirmed-close marker may survive an old ignored delivery.
        // A full server roster must allow a new complete proof to reconcile it.
        now=now.AddSeconds(2);
        store.ImportActiveServerRoster(data.RootElement,boundary);
        var fresh=new RadarSnapshot{CapturedAtUtc=now,WorldCharacterDataAvailable=true,LivePlayerPositionAvailable=true,
            IsInsideCenterZone=true,PlayerX=0,PlayerY=0,Traders=[new(7,"Present",1,200,200,0,true,now)]};
        if(store.ReconcileCenterRadar(fresh,center,now.AddSeconds(-1),"roster-again",true)!=1)
            throw new Exception("Server-active locally closed legacy identity must be reconciled again");
        Console.WriteLine("PASS server-only roster imports before center absence reconciliation without inventing evidence");
    }
}
