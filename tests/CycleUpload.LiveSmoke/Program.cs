using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

// Explicit saved-capture replay only; no driver access and no game actions.
if(args.Length!=4) throw new ArgumentException("broker.json prices.json market outbox-directory");
var b=JsonSerializer.Deserialize<BrokerInventoryFile>(File.ReadAllText(args[0]))!;
var names=b.Bindings.ToDictionary(t=>t.ObjectId);
var profile=new CollectorProfile {Name=args[2]};
var outbox=new ServerUploadOutbox(()=>{},args[3]);
var rows=b.Rows.Where(r=>names.ContainsKey(r.TraderObjectId)).Select(r=>new BrokerInventoryRow {
    ItemId=r.ItemId,TraderObjectId=r.TraderObjectId,TraderName=names[r.TraderObjectId].Name,Amount=r.Amount,StoreType=r.StoreType}).ToArray();
var inventory=new BrokerInventoryFile {Complete=false,StartedAtUtc=b.StartedAtUtc,CapturedAtUtc=b.CapturedAtUtc,Rows=rows};
var radar=new RadarSnapshot {Traders=b.Bindings.Select(t=>new RadarPoint(t.ObjectId,t.Name,t.KioskType,t.X,t.Y,0,true,b.CapturedAtUtc)).ToArray()};
await outbox.EnqueueCycleBrokerAsync(profile,inventory,radar);
using var p=JsonDocument.Parse(File.ReadAllText(args[1]));
var count=0;
foreach(var shop in p.RootElement.GetProperty("shops").EnumerateArray())
{
    if(shop.GetProperty("precision").GetString()!="wire_int64") continue;
    var oid=shop.GetProperty("trader").GetProperty("object_id").GetInt32();
    if(!names.TryGetValue(oid,out var t)) continue;
    var at=DateTimeOffset.Parse(shop.GetProperty("at").GetString()!);
    var target=new CycleTarget(CycleQueue.Key(t.Name),t.Name,t.ObjectId,t.KioskType,t.X,t.Y,1,"Live validation",at);
    var capture=new ShopCaptureFile {CapturedAtUtc=at,SnapshotId=$"live-{at.UtcTicks}-{oid}",Precision="wire_int64",Side="sell",
        Rows=shop.GetProperty("rows").EnumerateArray().Select((r,i)=>new ShopCaptureRow {RowIndex=i,
            ItemId=r.GetProperty("item_id").GetInt64(),ItemObjectId=r.GetProperty("item_object_id").GetInt64(),
            Quantity=r.GetProperty("quantity").GetInt64(),Price=r.GetProperty("price").GetInt64(),
            BasePrice=r.GetProperty("base_price").GetInt64(),EnchantLevel=r.GetProperty("enchant").GetInt32()}).ToArray()};
    await outbox.EnqueueCyclePriceAsync(profile,target,capture);count++;
}
await outbox.EnqueueCycleStatusAsync(profile,new(){Phase="Stopped",Detail="Bounded live validation finished",Checked=count,Active=names.Count});
Console.WriteLine($"LIVE_UPLOAD_PREPARED broker={names.Count} exactShops={count} authoritativeAbsence=false directory={args[3]}");
