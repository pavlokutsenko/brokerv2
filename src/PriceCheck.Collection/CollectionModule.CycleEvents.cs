using System.Collections.Concurrent;
using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private void ProcessCycleEvent(CycleRun cycle,string prefix,IReadOnlyDictionary<string,CycleTarget> targets,
        ConcurrentDictionary<string,byte> spooled,string line)
    {
        if(line.Length==0)return;
        using var doc=JsonDocument.Parse(line);var shop=doc.RootElement;
        var type=shop.GetProperty("type").GetString();
        if(type=="request"&&shop.GetProperty("action").GetInt32()==1)
        {
            var key=CycleQueue.Key(shop.GetProperty("trader_key").GetString()!);
            Log($"INFO {cycle.UploadProfile.Name}: trader={key} · exact shop read started");return;
        }
        if(type is "invalid_reply" or "timeout")
        {
            var key=CycleQueue.Key(shop.GetProperty("trader").GetProperty("name").GetString()!);
            var reason=type=="invalid_reply"?$"Invalid exact shop reply: {shop.GetProperty("error").GetString()}":
                $"Shop response timed out after {shop.GetProperty("actions").GetInt32()} normal actions";
            cycle.Store.RecordError(key,reason);return;
        }
        if(type=="shop_closed")
        {
            var key=CycleQueue.Key(shop.GetProperty("key").GetString()!);var target=cycle.Store.Target(key);
            if(target is not null)cycle.Store.ObserveClosed(new((int)shop.GetProperty("object_id").GetInt64(),target.Name,0,
                target.X,target.Y,0,true,shop.GetProperty("at").GetDateTimeOffset()),cycle.Center!.Value,cycle.CenterEnteredAt,false);
            return;
        }
        if(type!="shop")return;
        var keyRead=CycleQueue.Key(shop.GetProperty("trader_key").GetString()!);
        targets.TryGetValue(keyRead,out var assigned);var targetRead=assigned??ProvisionalCycleTarget(shop);
        if(targetRead is null)return;
        targetRead=ResolveContinuationTarget(shop,targetRead);if(targetRead is null)return;
        var capture=ExactCycleCapture(shop,targetRead,prefix);
        if(capture is null||spooled.ContainsKey(capture.SnapshotId))return;
        CommitLocalCapture(cycle,targetRead,capture,shop);
        spooled.TryAdd(capture.SnapshotId,0);spooled.TryAdd(keyRead,0);
        cycle.RadarPool.Capture(keyRead);cycle.Bindings.AcceptExact(targetRead,capture.CapturedAtUtc);
    }
}
