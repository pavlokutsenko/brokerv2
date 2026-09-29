using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PriceCheck.Collector.Services;

internal static class ParallelDeliveryTests
{
    public static async Task Run(string root)
    {
        var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
        using var stop=new CancellationTokenSource();
        var handlers=new ConcurrentBag<Task>();var activeKeys=new ConcurrentDictionary<string,int>();
        var received=new ConcurrentDictionary<string,List<int>>();
        var active=0;var maximum=0;var overlap=0;var retry=0;var total=0;
        var releaseSlow=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var priceStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server=Task.Run(async()=>{
            while(!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try{context=await listener.GetContextAsync().WaitAsync(stop.Token);}catch(OperationCanceledException){break;}
                handlers.Add(Task.Run(async()=>{
                    using var reader=new StreamReader(context.Request.InputStream);
                    using var body=JsonDocument.Parse(await reader.ReadToEndAsync());
                    var key=body.RootElement.GetProperty("traderKey").GetString()!;
                    var ordinal=body.RootElement.GetProperty("ordinal").GetInt32();
                    var count=Interlocked.Increment(ref active);
                    lock(received){maximum=Math.Max(maximum,count);received.GetOrAdd(key,_=>[]).Add(ordinal);}
                    if(activeKeys.AddOrUpdate(key,1,(_,old)=>old+1)!=1)Interlocked.Increment(ref overlap);
                    try
                    {
                        if(key=="SLOW"){slowStarted.TrySetResult();await releaseSlow.Task;}
                        if(key=="PRICE")priceStarted.TrySetResult();
                        await Task.Delay(70);
                        if(key=="RETRY" && ordinal==0 && Interlocked.Increment(ref retry)==1)context.Response.StatusCode=500;
                        var bytes=Encoding.UTF8.GetBytes("{\"accepted\":true,\"current\":true}");
                        context.Response.ContentLength64=bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes);context.Response.Close();
                        Interlocked.Increment(ref total);
                    }
                    finally{activeKeys.AddOrUpdate(key,0,(_,old)=>old-1);Interlocked.Decrement(ref active);}
                }));
            }
        });
        var profile=new PriceCheck.Collector.Models.CollectorProfile{Name="PARALLEL-DELIVERY",ServerUrl=$"http://127.0.0.1:{port}"};
        try
        {
            using(var store=new LocalCycleStore(profile,root))
            {
                Add(store,"slow","broker","SLOW",0);
                foreach(var i in Enumerable.Range(0,64))
                {Add(store,$"b-{i}-0","broker","B"+i,0);Add(store,$"b-{i}-1","broker","B"+i,1);}
                Add(store,"retry-state","state","RETRY",0);Add(store,"retry-price","price","RETRY",1);
                store.WakeSender();await slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Add(store,"fresh-price","price","PRICE",0);
                await priceStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!releaseSlow.Task.IsCompleted,"fresh price delivered while unrelated broker request is blocked");
                releaseSlow.SetResult();await store.SendPendingAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Require(store.PendingUploads==0,"all successful receipts drain the durable outbox");
                Require(maximum is >1 and <=8 && overlap==0,"bounded parallel delivery never overlaps one trader");
                Require(received.Where(pair=>pair.Key.StartsWith("B")).All(pair=>pair.Value.SequenceEqual(new[]{0,1})),"broker generations keep per-trader FIFO order");
                Require(received["RETRY"].SequenceEqual(new[]{0,0,1}),"failed state retries before later price and is not lost");
                Require(total==133,"no duplicate successful deliveries or lost operations");
            }
            using var restored=new LocalCycleStore(profile,root);
            Require(restored.PendingUploads==0,"successful parallel acknowledgements survive restart");
            Add(restored,"undelivered","broker","PENDING",0);
        }
        finally{releaseSlow.TrySetResult();stop.Cancel();listener.Stop();await server;await Task.WhenAll(handlers);}
        using var pending=new LocalCycleStore(profile,root);
        Require(pending.PendingOperations().Single().Id=="undelivered","stopping preserves undelivered operation for next launch");
        Console.WriteLine("PARALLEL DELIVERY PASS concurrency, FIFO, priority, retries, durable restart");
    }
    private static void Add(LocalCycleStore store,string id,string kind,string key,int ordinal)
    {
        var sync=typeof(LocalCycleStore).GetField("_sync",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(store)!;
        lock(sync)typeof(LocalCycleStore).GetMethod("AddOutbox",BindingFlags.Instance|BindingFlags.NonPublic)!
            .Invoke(store,[id,kind,kind,new{traderKey=key,ordinal}]);
    }
    private static void Require(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
}
