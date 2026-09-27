using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed partial class ServerUploadOutbox
{
    public async Task<string> EnqueueCycleBrokerAsync(CollectorProfile profile, BrokerInventoryFile inventory, RadarSnapshot radar,
        string? workerId=null,IReadOnlySet<string>? reopened=null)
    {
        var positions=radar.Traders.GroupBy(t=>CycleQueue.Key(t.Name)).ToDictionary(g=>g.Key,g=>g.Last());
        var traders=inventory.Rows.Where(r=>r.TraderName.Length>0).GroupBy(r=>CycleQueue.Key(r.TraderName)).Select(group=>
        {
            var first=group.First();positions.TryGetValue(group.Key,out var position);
            return new { traderKey=group.Key,displayName=first.TraderName,objectId=first.TraderObjectId.ToString(),
                kioskType=first.StoreType,x=position?.X,y=position?.Y,z=(double?)null,
                inventoryComplete=inventory.Complete,reopened=reopened?.Contains(group.Key)??false,
                items=group.GroupBy(r=>new { r.ItemId,Side=r.StoreType==3?"Buy":"Sell" })
                    .Select(g=>new {itemId=g.Key.ItemId.ToString(),side=g.Key.Side,
                        quantity=g.Sum(r=>r.Amount).ToString(),isPackage=g.Any(r=>r.StoreType==8),listingCount=g.Count()}).ToArray() };
        }).ToArray();
        var batchId=$"broker-{profile.Id:N}-{Guid.NewGuid():N}";
        await EnqueueAsync("broker",SnapshotUrl(profile),new {schemaVersion=2,kind="broker",
            batchId,sourceId=workerId??$"collector:{profile.Id:N}",
            market=profile.Name,city=profile.City,observedAtUtc=inventory.CapturedAtUtc,
            startedAtUtc=inventory.StartedAtUtc,completePresence=inventory.Complete,
            epochComplete=inventory.Complete,storeTypes=new[]{1,3,8},traders});
        return batchId;
    }

    public Task EnqueueCyclePriceAsync(CollectorProfile profile,CycleTarget target,ShopCaptureFile capture,
        string? workerId=null,bool admitNewFromRadar=false) =>
        EnqueueAsync("price-snapshot",$"{BaseUrl(profile)}/ingest/price-snapshot",new {
            schemaVersion=2,snapshotId=capture.SnapshotId,sourceId=$"collector:{profile.Id:N}",market=profile.Name,city=profile.City,
            traderKey=target.TraderKey,displayName=target.Name,objectId=target.ObjectId.ToString(),kioskType=target.KioskType,
            x=target.X,y=target.Y,side=capture.Side,capturedAtUtc=capture.CapturedAtUtc,
            precision=capture.Precision,complete=true,admitNewFromRadar,
            queueLease=target.ServerJob is {} job?new {workerId,job.LeaseToken,job.Revision}:null,
            rows=capture.Rows.Select(r=>new {rowIndex=r.RowIndex,itemId=r.ItemId.ToString(),itemObjectId=r.ItemObjectId.ToString(),
                quantity=r.Quantity.ToString(),enchantLevel=r.EnchantLevel,price=r.Price.ToString(),
                buyCount=r.BuyCount.ToString(),basePrice=r.BasePrice.ToString()}).ToArray() });

    public Task EnqueueCycleReleaseAsync(CollectorProfile profile,string workerId,IReadOnlyList<MarketTaskRelease> jobs) =>
        jobs.Count==0?Task.CompletedTask:EnqueueAsync("queue-release",$"{BaseUrl(profile)}/v2/price-check-queue/release",new {
            market=profile.Name,city=profile.City,workerId,jobs=jobs.Select(j=>new {j.Job.TraderId,j.Job.LeaseToken,j.Job.Revision,error=j.Error}) });

    public Task EnqueueCycleStatusAsync(CollectorProfile profile,MarketCycleStatus state,string? workerId=null) =>
        EnqueueAsync("collector-status",$"{BaseUrl(profile)}/ingest/collector-status",new {
            sourceId=workerId??$"collector:{profile.Id:N}",market=profile.Name,city=profile.City,observedAtUtc=DateTimeOffset.UtcNow,
            phase=state.Phase,detail=state.Detail[..Math.Min(2000,state.Detail.Length)],active=state.Active,pending=state.Pending,deferred=state.Deferred,
            overdue=state.Overdue,lastBrokerAt=state.LastBrokerAt,nextBrokerAt=state.NextBrokerAt,queue=state.Queue });
}
