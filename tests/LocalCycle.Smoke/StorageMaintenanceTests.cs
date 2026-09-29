using System.Reflection;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class StorageMaintenanceTests
{
    public static void Run(string root)
    {
        var now=DateTimeOffset.UtcNow;
        using var store=new LocalCycleStore(new CollectorProfile{Name="MAINTENANCE"},root,()=>now);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var db=typeof(LocalCycleStore).GetField("_db",flags)!.GetValue(store)!;
        void Command(string sql,params object?[] values)=>db.GetType().GetMethod("Command")!.Invoke(db,[sql,values]);
        var traders=(Dictionary<string,LocalTrader>)typeof(LocalCycleStore).GetField("_traders",flags)!.GetValue(store)!;
        traders["SHOP"]=new(){Key="SHOP",Name="Shop",LastSnapshotId="anchor"};
        void Snapshot(string id,string key,int days)=>Command("INSERT INTO snapshots VALUES(?,?,?,?,?)",id,key,now.AddDays(-days).ToUnixTimeMilliseconds(),1,"{}");
        Snapshot("delivered-old","SHOP",8);Snapshot("anchor","SHOP",9);
        Snapshot("pending-old","SHOP",10);Snapshot("recent","SHOP",6);
        Snapshot("latest-old","OTHER",20);
        Command("INSERT INTO outbox(operation_id,kind,url,payload,next_attempt) VALUES(?,?,?,?,?)","pending-old","price","unused","{}",long.MaxValue);
        now=now.AddHours(2);
        if(store.MaintainStorage()!=1)throw new Exception("Only old delivered superseded capture should expire");
        var rows=(List<string?[]>)db.GetType().GetMethod("Query")!.Invoke(db,["SELECT snapshot_id FROM snapshots",Array.Empty<object?>()])!;
        var ids=rows.Select(r=>r[0]).ToHashSet();
        if(!ids.SetEquals(["anchor","pending-old","recent","latest-old"])||store.PendingUploads!=1)
            throw new Exception("Maintenance lost pending/current/latest/recent capture");
        if(store.MaintainStorage()!=0)throw new Exception("Maintenance is not throttled");
        Command("DELETE FROM outbox WHERE operation_id=?","pending-old");now=now.AddHours(2);
        if(store.MaintainStorage()!=1)throw new Exception("Successfully delivered old capture did not expire");
        Console.WriteLine("STORAGE_MAINTENANCE_OK delivered expiry pending/current/latest preservation hourly throttle");
    }
}
