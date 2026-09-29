using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private static CycleTarget? ProvisionalCycleTarget(JsonElement shop)
    {
        if(shop.GetProperty("precision").GetString()!="wire_int64")return null;
        var trader=shop.GetProperty("trader");
        var key=CycleQueue.Key(shop.GetProperty("trader_key").GetString()!);
        var name=trader.GetProperty("name").GetString();
        var id=trader.GetProperty("object_id").GetInt64();var kiosk=trader.GetProperty("kiosk_type").GetInt32();
        var x=trader.GetProperty("x").GetDouble();var y=trader.GetProperty("y").GetDouble();
        if(name is null || key.Length==0 || id<=0 || kiosk is not(1 or 3 or 8) || !double.IsFinite(x) || !double.IsFinite(y))return null;
        return new(key,name,id,kiosk,x,y,0,"Radar capture",DateTimeOffset.UtcNow);
    }
    private static ShopCaptureFile? ExactCycleCapture(JsonElement shop,CycleTarget target,string prefix)
    {
        if(shop.GetProperty("precision").GetString()!="wire_int64" || shop.GetProperty("trader").GetProperty("object_id").GetInt64()!=target.ObjectId)return null;
        var rows=shop.GetProperty("rows").EnumerateArray().Select((r,i)=>new ShopCaptureRow {
            RowIndex=i,ItemId=r.GetProperty("item_id").GetInt64(),ItemObjectId=r.GetProperty("item_object_id").GetInt64(),
            Quantity=r.GetProperty("quantity").GetInt64(),EnchantLevel=r.GetProperty("enchant").GetInt32(),
            BuyCount=r.TryGetProperty("buy_count",out var buy)?buy.GetInt64():shop.GetProperty("side").GetString()=="buy"?r.GetProperty("quantity").GetInt64():0,
            Price=r.GetProperty("price").GetInt64(),BasePrice=r.GetProperty("base_price").GetInt64() }).ToArray();
        return new() {Side=shop.GetProperty("side").GetString()!,Rows=rows,
            ReadStartedAtUtc=shop.TryGetProperty("read_started_at",out var readStarted)&&readStarted.ValueKind==JsonValueKind.String?readStarted.GetDateTimeOffset():null,
            CapturedAtUtc=DateTimeOffset.Parse(shop.GetProperty("at").GetString()!,CultureInfo.InvariantCulture),Precision="wire_int64",
            SnapshotId=Path.GetFileName(prefix)+"-"+target.ObjectId+(shop.TryGetProperty("capture_sequence",out var sequence)?"-"+sequence.GetInt64():"")};
    }
    private static CycleTarget? ResolveContinuationTarget(JsonElement shop,CycleTarget target)
    {
        var current=ProvisionalCycleTarget(shop);
        if(current is null || current.TraderKey!=target.TraderKey)return null;
        // The worker froze these freshly validated fields before its first action.
        return current with {Revision=target.Revision,VerificationRevision=target.VerificationRevision,RebindOnRead=false};
    }
    private static bool ReopenedCapture(JsonElement shop)=>shop.TryGetProperty("reopened",out var value)&&value.ValueKind==JsonValueKind.True;
    private static CycleTarget? ReopenedCycleTarget(JsonElement shop,CycleTarget? assigned,bool alreadyUploaded)=>ProvisionalCycleTarget(shop);
    private static void CommitLocalCapture(CycleRun cycle,CycleTarget target,ShopCaptureFile capture,JsonElement shop)
    {
        var token=shop.TryGetProperty("verification_revision",out var frozen)&&frozen.ValueKind!=JsonValueKind.Null
            ? frozen.ValueKind==JsonValueKind.String?frozen.GetString():frozen.GetRawText()
            :target.VerificationRevision??target.Revision.ToString();
        var current=cycle.Store.Target(target.TraderKey);
        var revision=current is not null && current.VerificationRevision==token?current.Revision:target.Revision;
        // Unknown dynamic revision is safely historical; it cannot complete a new requirement.
        try{cycle.Store.Commit(target with {Revision=revision,VerificationRevision=token},capture);}
        catch(InvalidDataException e)
        {
            cycle.Store.FinishAttempt(target.TraderKey,e.Message);
            cycle.Store.RecordError(target.TraderKey,e.Message);
            return;
        }
        if(cycle.Store.Target(target.TraderKey) is not null)cycle.Store.FinishAttempt(target.TraderKey,"Shop changed while reading; captured result remains historical");
    }
    private async Task StreamCyclePricesAsync(CycleRun cycle,string prefix,IReadOnlyDictionary<string,CycleTarget> targets,
        ConcurrentDictionary<string,byte> spooled,CancellationToken stop)
    {
        var tail=cycle.PriceEventReader??=new AppendOnlyJsonLineReader(prefix+".shops.jsonl");
        while(!stop.IsCancellationRequested)
        {
            try
            {
                tail.VisitNewLines(line=>
                {
                    try{ProcessCycleEvent(cycle,prefix,targets,spooled,line);}
                    catch(JsonException e){Log($"WARNING {cycle.UploadProfile.Name}: malformed complete native event · {e.Message}");}
                },stop);
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException)
            {Log($"WARNING {cycle.UploadProfile.Name}: price persistence retry · {e.Message}");}
            await Task.Delay(100,stop);
        }
    }
}
