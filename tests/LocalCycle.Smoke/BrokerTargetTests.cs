using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class BrokerTargetTests
{
    public static void Run(string root)
    {
        var at=DateTimeOffset.UtcNow;
        var boundary=new CycleRadarPool([[-1000,-1000],[1000,-1000],[1000,1000],[-1000,1000]]);
        var point=new RadarPoint(7,"BrokerShop",1,100,100,0,true,at){StateObservedAtUtc=at};
        RadarSnapshot Frame()=>new(){Traders=[point],PlayerX=0,PlayerY=0,CapturedAtUtc=at,LivePlayerPositionAvailable=true};
        using var store=new LocalCycleStore(new(){Name="BROKER-TARGETS",ServerUrl="http://127.0.0.1:1"},root,()=>at);
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        BrokerInventoryRow Row(int item,long amount=10)=>new(){TraderName="BrokerShop",TraderObjectId=7,StoreType=1,ItemId=item,Amount=amount};
        void Broker(string id,bool complete,DateTimeOffset started,params BrokerInventoryRow[] rows)=>
            store.ApplyBroker(new(){Complete=complete,StartedAtUtc=started,CapturedAtUtc=at,Rows=rows},Frame(),id,boundary);
        void Read(string id,params (int Item,int Enchant)[] rows)
        {
            var target=store.Target("BROKERSHOP")??throw new Exception("Expected reread target");
            store.Commit(target,new(){SnapshotId=id,Precision="wire_int64",Side="sell",ReadStartedAtUtc=at,CapturedAtUtc=at,
                Rows=rows.Select((r,i)=>new ShopCaptureRow{RowIndex=i,ItemId=r.Item,ItemObjectId=99+i,Quantity=10,Price=100+i,EnchantLevel=r.Enchant,BasePrice=0}).ToArray()});
        }
        int StateCount()=>store.PendingOperations().Count(o=>o.Kind=="state");
        store.BeginSession("broker-targets");store.Observe(Frame(),boundary,new(0,0,500),at.AddSeconds(-1));
        at=at.AddMilliseconds(1);Read("broker-initial",(57,0));
        var stateBefore=StateCount();at=at.AddSeconds(1);Broker("quantity-complete",true,at.AddMilliseconds(-1),Row(57,77));
        Check(store.Target("BROKERSHOP")is null&&StateCount()==stateBefore,"Complete quantity-only change must not schedule exact price reread");
        at=at.AddSeconds(1);Broker("quantity-partial",false,at.AddMilliseconds(-1),Row(57,3));
        Check(store.Target("BROKERSHOP")is null&&StateCount()==stateBefore,"Partial quantity-only change must preserve successful read generation");
        foreach(var id in new[]{"quantity-complete","quantity-partial"})
        {
            var operation=store.PendingOperations().Single(o=>o.Kind=="broker"&&o.Id.StartsWith(id+"-"));
            using var body=JsonDocument.Parse(operation.Payload);
            Check(body.RootElement.GetProperty("trader").GetProperty("items")[0].GetProperty("quantity").GetString()==(id=="quantity-complete"?"77":"3"),"Changed broker amount must still be delivered individually");
        }
        at=at.AddSeconds(1);Broker("new-item",true,at.AddMilliseconds(-1),Row(57),Row(58));
        var added=store.Target("BROKERSHOP");
        Check(added is not null&&added.Reason=="New broker positions"&&StateCount()==stateBefore+1,"New item at known fresh trader must invalidate once");
        store.BeginPass(0,0,boundary);
        Check(store.NextTargets().Count==1&&store.NextTargets()[0].TraderKey=="BROKERSHOP","Finalized broker inserts exactly the changed trader into the next route segment");
        using(var state=JsonDocument.Parse(store.PendingOperations().Single(o=>o.Kind=="state").Payload))
            Check(state.RootElement.GetProperty("type").GetString()=="new_listings","Known trader discovery must send new_listings freshness event");
        at=at.AddMilliseconds(1);Read("new-item-verified",(57,0),(58,3));
        Check(store.NextTargets().Count==0,"Successful reread must finish this changed trader in the route");
        stateBefore=StateCount();at=at.AddSeconds(1);Broker("complete-removal",true,at.AddMilliseconds(-1),Row(57,4));
        Check(store.Target("BROKERSHOP")is null&&StateCount()==stateBefore,"Complete missing item removal must update stock without rereading unchanged prices");
        var removed=store.PendingOperations().Single(o=>o.Kind=="broker"&&o.Id.StartsWith("complete-removal-"));
        using(var body=JsonDocument.Parse(removed.Payload))
            Check(body.RootElement.GetProperty("compositionComplete").GetBoolean()&&body.RootElement.GetProperty("trader").GetProperty("items").GetArrayLength()==1,"Complete omission must authorize only stock composition update");
        at=at.AddSeconds(1);Broker("new-physical-row",false,at.AddMilliseconds(-1),Row(57,2),Row(57,2));
        Check(store.Target("BROKERSHOP")is not null&&StateCount()==stateBefore+1,"Additional physical row of same item must require fresh price/enchant read");
        store.BeginPass(0,0,boundary);at=at.AddMilliseconds(1);Read("physical-row-verified",(57,0),(57,5));
        stateBefore=StateCount();at=at.AddSeconds(1);Broker("physical-quantity-only",true,at.AddMilliseconds(-1),Row(57,1),Row(57,3));
        Check(store.Target("BROKERSHOP")is null&&StateCount()==stateBefore,"Same physical composition with changed quantities must not reread either enchant variant");
        at=at.AddSeconds(1);
        ReadDue();
        void ReadDue()
        {
            store.SetRecheckHours(0.1);at=at.AddHours(1);var epoch=at;
            Read("same-time-read",(57,0));
            Broker("equal-epoch-new-item",true,epoch,Row(57),Row(59));
            Check(store.Target("BROKERSHOP")is null,"Successful read at epoch start is protected from broker composition invalidation");
        }
        Console.WriteLine("BROKER_TARGETS_OK quantity_only_no_reread new_item_one_route_target same_item_new_row reread complete_removal_stock_only equal_epoch_protected");
    }
}
