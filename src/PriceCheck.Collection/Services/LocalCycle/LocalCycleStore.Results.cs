using System.Text.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed partial class LocalCycleStore
{
    public void RecordError(string key,string reason)
    {lock(_sync)Transaction(()=>{if(_traders.TryGetValue(key,out var t)){t.Error=reason;Save(t);}});Message?.Invoke($"WARNING {_profile.Name}: trader={key} · {reason}");}
    public string? ErrorFor(string key){lock(_sync)return _traders.GetValueOrDefault(key)?.Error;}
    public CycleTarget? Target(string key)
    {
        lock(_sync) return _traders.TryGetValue(key,out var t) && Ready(t)
            ?new(t.Key,t.Name,t.ObjectId,t.KioskType,t.X,t.Y,t.Revision,t.Reason,t.ChangedAt){RebindOnRead=t.ObjectId==0,VerificationRevision=t.VerificationToken}:null;
    }
    public bool Commit(CycleTarget target,ShopCaptureFile capture)
    {
        if(capture.Precision!="wire_int64" || capture.SnapshotId.Length==0 || capture.Rows.Count==0 ||
            capture.Rows.Any(r=>r.ItemId<=0 || r.Quantity<=0 || r.Price<0 || r.EnchantLevel<0) ||
            target.KioskType is not (1 or 3 or 8)) throw new InvalidDataException("Shop capture is not a complete valid exact snapshot.");
        lock(_sync)
        {
            if(_db.Query("SELECT snapshot_id FROM snapshots WHERE snapshot_id=?",capture.SnapshotId).Count>0) return false;
            var payload=new {schemaVersion=2,snapshotId=capture.SnapshotId,sourceId=$"collector:{_sourceProfile:N}",market=_profile.Name,city="Giran",
                traderKey=target.TraderKey,displayName=target.Name,objectId=target.ObjectId.ToString(),kioskType=target.KioskType,
                x=target.X,y=target.Y,side=capture.Side,capturedAtUtc=capture.CapturedAtUtc,readStartedAtUtc=capture.ReadStartedAtUtc,precision="wire_int64",complete=true,
                admitNewFromRadar=true,verificationRevision=target.VerificationRevision??target.Revision.ToString(),isPackage=target.KioskType==8,
                rows=capture.Rows.Select(r=>new {rowIndex=r.RowIndex,itemId=r.ItemId.ToString(),itemObjectId=r.ItemObjectId.ToString(),
                    quantity=r.Quantity.ToString(),enchantLevel=r.EnchantLevel,price=r.Price.ToString(),buyCount=r.BuyCount.ToString(),
                    basePrice=r.BasePrice.ToString(),isPackage=target.KioskType==8}).ToArray()};
            Transaction(()=>{
                if(!_traders.ContainsKey(target.TraderKey))
                {
                    var observed=new LocalTrader{Key=target.TraderKey,Name=target.Name,X=target.X,Y=target.Y,HasPosition=true,
                        KioskType=target.KioskType,ObjectId=target.ObjectId,ChangedAt=capture.CapturedAtUtc,ObservedAt=capture.CapturedAtUtc,
                        Revision=Math.Max(1,target.Revision),VerificationToken=target.VerificationRevision??target.Revision.ToString()};
                    _traders.Add(observed.Key,observed);Save(observed);
                }
                _db.Command("INSERT INTO snapshots VALUES(?,?,?,?,?)",capture.SnapshotId,target.TraderKey,capture.CapturedAtUtc.ToUnixTimeMilliseconds(),target.Revision,JsonSerializer.Serialize(payload,Json));
                AddOutbox(capture.SnapshotId,"price","price",payload);
                if(_traders.TryGetValue(target.TraderKey,out var t) && t.Revision==target.Revision &&
                    t.VerificationToken==(target.VerificationRevision??target.Revision.ToString()) && !t.ClosedThisSession &&
                    (capture.ReadStartedAtUtc??capture.CapturedAtUtc)>=t.ChangedAt && (t.LastRead is null || capture.CapturedAtUtc>=t.LastRead))
                {
                    t.LastRead=capture.CapturedAtUtc;t.LastSnapshotId=capture.SnapshotId;t.NeedsServerHistory=false;t.Dirty=false;t.Error=null;t.CheckedX=target.X;t.CheckedY=target.Y;
                    t.Composition=capture.Rows.GroupBy(r=>$"{r.ItemId}:{capture.Side.ToLowerInvariant()}").ToDictionary(g=>g.Key,g=>g.Count());
                    Save(t);_remaining.Remove(t.Key);_readPass.Add(t.Key);
                    _db.Command("UPDATE route SET done=1 WHERE pass_id=? AND trader_key=?",_pass,t.Key);
                }
            });
            var elapsed=capture.ReadStartedAtUtc is {} started?$" · duration_ms={(capture.CapturedAtUtc-started).TotalMilliseconds:F0}":"";
            Message?.Invoke($"INFO {_profile.Name}: {target.Name} trader={target.TraderKey} · exact read {capture.Rows.Count} rows{elapsed} · durable snapshot {capture.SnapshotId} · background send queued");
        }
        WakeSender();return true;
    }
    public void ApplyBroker(BrokerInventoryFile inventory,RadarSnapshot radar,string epochId,CycleRadarPool? boundary=null)
    {
        lock(_sync) Transaction(()=>{
            if(boundary is not null)_collectionBoundary=boundary;
            var points=radar.Traders.GroupBy(t=>CycleQueue.Key(t.Name)).ToDictionary(g=>g.Key,g=>g.Last());
            foreach(var group in inventory.Rows.Where(r=>r.TraderName.Length>0).GroupBy(r=>CycleQueue.Key(r.TraderName)))
            {
                var first=group.First();points.TryGetValue(group.Key,out var p);
                if(p is not null && boundary is not null && !boundary.Inside(p.X,p.Y))
                {
                    Message?.Invoke($"INFO {_profile.Name}: trader={group.Key} · broker observation outside approved Giran boundary ignored");
                    continue;
                }
                if(!_traders.TryGetValue(group.Key,out var t))
                { t=new(){Key=group.Key,Name=first.TraderName,KioskType=first.StoreType,ChangedAt=inventory.CapturedAtUtc};_traders.Add(t.Key,t); }
                if(p is not null){t.X=p.X;t.Y=p.Y;t.ObjectId=p.ObjectId;t.HasPosition=true;}
                var composition=group.GroupBy(r=>$"{r.ItemId}:{(r.StoreType==3?"buy":"sell")}").ToDictionary(g=>g.Key,g=>g.Count());
                var newPositions=composition.Any(pair=>!t.Composition.TryGetValue(pair.Key,out var count)||pair.Value>count);
                if(newPositions && t.LastRead is not null && t.LastRead<inventory.StartedAtUtc)
                {
                    Invalidate(t,"New broker positions",inventory.CapturedAtUtc);
                    StateEvent(t,"new_listings",inventory.CapturedAtUtc,p,new(0,0,500));
                }
                if(inventory.Complete)t.Composition=composition;
                else foreach(var pair in composition)t.Composition[pair.Key]=Math.Max(t.Composition.GetValueOrDefault(pair.Key),pair.Value);
                Save(t);
                var keyHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(t.Key)))[..24];
                var id=$"{epochId}-{keyHash}";
                AddOutbox(id,"broker","broker",new {operationId=id,epochId,sourceId=$"collector:{_sourceProfile:N}",market=_profile.Name,city="Giran",
                    startedAtUtc=inventory.StartedAtUtc,observedAtUtc=inventory.CapturedAtUtc,compositionComplete=inventory.Complete,
                    trader=new {traderKey=t.Key,displayName=t.Name,objectId=first.TraderObjectId.ToString(),kioskType=first.StoreType,x=p?.X,y=p?.Y,
                        items=group.GroupBy(r=>new{r.ItemId,side=r.StoreType==3?"Buy":"Sell"}).Select(g=>new {itemId=g.Key.ItemId.ToString(),g.Key.side,
                            quantity=g.Sum(r=>r.Amount).ToString(),listingCount=g.Count(),isPackage=g.Any(r=>r.StoreType==8)}).ToArray()}});
            }
        });
        WakeSender();
    }
}
