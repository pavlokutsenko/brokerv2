using System.Reflection;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class BrokerDeliveryStatusTests
{
    public static void Run(string root)
    {
        var profile=new CollectorProfile{Name="BROKER-DELIVERY-STATUS",ServerUrl="http://127.0.0.1:1"};
        var at=DateTimeOffset.Parse("2026-09-28T01:00:00Z");
        var radar=new RadarSnapshot{Traders=[Point("A",1,at),Point("B",2,at)]};
        using(var store=new LocalCycleStore(profile,root,()=>at))
        {
            store.ApplyBroker(Inventory(at,"A","B"),radar,"epoch-one");
            Require(store.LatestBrokerDelivery is {Traders:2,Accepted:0},"latest broker starts with two traders and no receipts");
            var first=store.PendingOperations().Single(op=>op.Kind=="broker" && op.Payload.Contains("epoch-one") && op.Payload.Contains("\"traderKey\":\"A\""));
            Ack(store,first);
            Require(store.LatestBrokerDelivery is {Traders:2,Accepted:1},"successful broker receipt increments trader count");
            Ack(store,first);
            Require(store.LatestBrokerDelivery is {Traders:2,Accepted:1},"duplicate receipt cannot increment count");
            store.ApplyBroker(Inventory(at.AddMinutes(1),"B"),radar,"epoch-two");
            Require(store.LatestBrokerDelivery is {Traders:1,Accepted:0},"new broker epoch resets latest delivery counter");
            var old=store.PendingOperations().FirstOrDefault(op=>op.Kind=="broker" && op.Payload.Contains("epoch-one"));
            if(old is not null) Ack(store,old);
            Require(store.LatestBrokerDelivery is {Traders:1,Accepted:0},"late older epoch does not count toward latest upload");
            Ack(store,store.PendingOperations().Single(op=>op.Kind=="broker" && op.Payload.Contains("epoch-two")));
        }
        using var restored=new LocalCycleStore(profile,root,()=>at);
        Require(restored.LatestBrokerDelivery is {Traders:1,Accepted:1},"latest successful count survives restart");
    }

    private static RadarPoint Point(string name,int id,DateTimeOffset at)=>new(id,name,1,0,0,0,true,at);
    private static BrokerInventoryFile Inventory(DateTimeOffset at,params string[] names)=>new()
    {
        Complete=true,StartedAtUtc=at,CapturedAtUtc=at,
        Rows=names.Select((name,index)=>new BrokerInventoryRow{TraderName=name,TraderObjectId=index+1,StoreType=1,ItemId=57,Amount=1}).ToArray()
    };
    private static void Ack(LocalCycleStore store,LocalCycleOperation operation)
    {
        var method=typeof(LocalCycleStore).GetMethod("ApplyUploadAcknowledgement",BindingFlags.Instance|BindingFlags.NonPublic)!;
        method.Invoke(store,[new string?[]{operation.Id,operation.Kind,"",operation.Payload,"0",""},""]);
    }
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
}
