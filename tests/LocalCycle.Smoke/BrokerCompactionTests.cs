using System.Reflection;
using PriceCheck.Collector.Services;

internal static class BrokerCompactionTests
{
    public static void Run(string root)
    {
        var profile=new PriceCheck.Collector.Models.CollectorProfile{Name="BROKER-COMPACTION",ServerUrl="http://127.0.0.1:1"};
        var at=DateTimeOffset.Parse("2026-09-27T23:00:00Z");
        using(var store=new LocalCycleStore(profile,root,()=>at))
        {
            Add(store,"b1","broker",at.AddSeconds(1));
            Add(store,"b2","broker",at.AddSeconds(2));
            Require(store,"b2");

            Add(store,"price","price",at.AddSeconds(3));
            Add(store,"b3","broker",at.AddSeconds(4));
            Add(store,"b4","broker",at.AddSeconds(5),complete:false);
            Require(store,"b2","price","b3","b4");
            Add(store,"b5","broker",at.AddSeconds(6));
            Require(store,"b2","price","b5");

            Add(store,"other-source","broker",at.AddSeconds(7),source:"collector:other");
            Add(store,"b7","broker",at.AddSeconds(8));
            var sending=(HashSet<string>)typeof(LocalCycleStore).GetField("_sendingOperations",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(store)!;
            sending.Add("b7");
            Add(store,"b8","broker",at.AddSeconds(9));
            Require(store,"b2","price","b5","other-source","b7","b8");
            sending.Remove("b7");
            Add(store,"b9","broker",at.AddSeconds(10));
            Require(store,"b2","price","b5","other-source","b9");
            Add(store,"b9","broker",at.AddSeconds(10));
            Require(store,"b2","price","b5","other-source","b9");
            Add(store,"older","broker",at.AddSeconds(9));
            Require(store,"b2","price","b5","other-source","b9","older");
        }
        using var restored=new LocalCycleStore(profile,root,()=>at);
        Require(restored,"b2","price","b5","other-source","b9","older");
        Console.WriteLine("BROKER COMPACTION PASS complete supersession, barriers, in-flight, stale time, restart");
    }

    private static void Add(LocalCycleStore store,string id,string kind,DateTimeOffset at,bool complete=true,string source="collector:own")
    {
        var sync=typeof(LocalCycleStore).GetField("_sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(store)!;
        lock(sync)typeof(LocalCycleStore).GetMethod("AddOutbox",BindingFlags.Instance|BindingFlags.NonPublic)!
            .Invoke(store,[id,kind,kind,new{traderKey="SHOP",sourceId=source,compositionComplete=complete,observedAtUtc=at}]);
    }

    private static void Require(LocalCycleStore store,params string[] expected)
    {
        var actual=store.PendingOperations().Select(operation=>operation.Id).ToArray();
        if(!actual.SequenceEqual(expected))throw new Exception($"Broker compaction: expected {string.Join(',',expected)}, got {string.Join(',',actual)}");
    }
}
