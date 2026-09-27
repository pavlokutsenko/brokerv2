using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class AcknowledgementTests
{
    public static async Task Run(string root)
    {
        await Scenario(root,"latest-historical",false,"{\"accepted\":true,\"current\":false}",true);
        await Scenario(root,"old-historical",true,"{\"accepted\":true,\"current\":false}",false);
        await Scenario(root,"legacy-ack",false,"{\"accepted\":true}",false);
        await Scenario(root,"string-current",false,"{\"accepted\":true,\"current\":\"false\"}",false);
        Console.WriteLine("ACKNOWLEDGEMENT PASS latest generation, delayed old result, durable recovery, older server requirement, legacy ACK");
    }

    private static async Task Scenario(string root,string name,bool newerRead,string firstAck,bool rejectedLatest)
    {
        var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
        using var stop=new CancellationTokenSource();
        var requests=new ConcurrentQueue<string>();var handlers=new ConcurrentBag<Task>();
        var priceStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrice=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var now=DateTimeOffset.UtcNow;var oldRequired=now.AddHours(-1);const string serverToken="server-older-required-token";
        var server=Task.Run(async()=>{
            while(!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try{context=await listener.GetContextAsync().WaitAsync(stop.Token);}catch(OperationCanceledException){break;}
                handlers.Add(Task.Run(async()=>{
                    var kind=context.Request.Url!.AbsolutePath.Split('/').Last();
                    using var reader=new StreamReader(context.Request.InputStream);
                    using var body=JsonDocument.Parse(await reader.ReadToEndAsync());requests.Enqueue(kind);
                    var response="{\"accepted\":true,\"current\":true}";
                    if(kind=="price"&&body.RootElement.GetProperty("snapshotId").GetString()==name+"-first")
                    {priceStarted.TrySetResult();await releasePrice.Task;response=firstAck;}
                    if(kind=="history")response=JsonSerializer.Serialize(new{traders=new[]{new{traderKey="SHOP",isActive=true,
                        lastReadAtUtc=now.AddHours(-2),verificationRequiredAtUtc=oldRequired,verificationRevision=serverToken,
                        checkedX=100,checkedY=100,lastReadKioskType=1}}});
                    var bytes=Encoding.UTF8.GetBytes(response);context.Response.ContentType="application/json";
                    context.Response.ContentLength64=bytes.Length;await context.Response.OutputStream.WriteAsync(bytes);context.Response.Close();
                }));
            }
        });
        var profile=new CollectorProfile{Name="ACK-"+name,ServerUrl=$"http://127.0.0.1:{port}"};
        DateTimeOffset captured=now;
        try
        {
            using(var store=new LocalCycleStore(profile,root,()=>now))
            {
                store.BeginSession("ack-session");Observe(store,now);store.BeginPass(0,0,new());
                var target=store.Target("SHOP")!;now=now.AddMilliseconds(1);captured=now;
                store.Commit(target,Capture(name+"-first",now));await priceStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await store.ReconcileAsync();
                Require(!Trader(store).Dirty,"undelivered own capture is not invalidated by older server generation");
                if(newerRead)
                {now=now.AddMilliseconds(1);captured=now;store.Commit(target,Capture(name+"-newer",now));}
                releasePrice.TrySetResult();await WaitUntil(()=>store.PendingUploads==0);
                Require(requests.Count(kind=>kind=="price")== (newerRead?2:1),"2xx historical ACK never causes a transport retry");
                Require(Trader(store).LastRead==captured,"acknowledgement keeps actual successful local read time");
                Require(Trader(store).Dirty==rejectedLatest && Trader(store).NeedsServerHistory==rejectedLatest,
                    "only current:false for latest still-clean capture creates durable verification need");
                if(newerRead)Require(Trader(store).LastSnapshotId==name+"-newer","late obsolete ACK preserves newer own capture identity");
            }
            using var recovered=new LocalCycleStore(profile,root,()=>now);
            Require(recovered.PendingUploads==0,"delivered snapshot is absent from outbox after restart");
            Require(Trader(recovered).LastRead==captured,"local history survives acknowledgement and restart");
            if(rejectedLatest)
            {
                Require(Trader(recovered).Dirty&&Trader(recovered).NeedsServerHistory,"rejected current generation survives restart as dirty");
                await recovered.ReconcileAsync();
                var required=recovered.Target("SHOP");
                Require(required?.VerificationRevision==serverToken,"older server requirement adopts its generation token for next read");
                Require(Trader(recovered).LastRead==captured&&!Trader(recovered).NeedsServerHistory,"reconciliation keeps own last-read history and clears waiting state");
            }
        }
        finally
        {releasePrice.TrySetResult();stop.Cancel();listener.Stop();await server;await Task.WhenAll(handlers);}
    }

    private static void Observe(LocalCycleStore store,DateTimeOffset now)=>store.Observe(new(){LivePlayerPositionAvailable=true,
        Traders=[new RadarPoint(7,"Shop",1,100,100,0,true,now){StateObservedAtUtc=now}],CapturedAtUtc=now},new(),new(0,0,500),now.AddMinutes(-1));
    private static ShopCaptureFile Capture(string id,DateTimeOffset now)=>new(){SnapshotId=id,Precision="wire_int64",Side="sell",
        ReadStartedAtUtc=now,CapturedAtUtc=now,Rows=[new(){RowIndex=0,ItemId=57,Quantity=1,Price=123,ItemObjectId=9,BasePrice=1}]};
    private static LocalTrader Trader(LocalCycleStore store)=>((Dictionary<string,LocalTrader>)typeof(LocalCycleStore)
        .GetField("_traders",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(store)!).Values.Single();
    private static async Task WaitUntil(Func<bool> condition)
    {var timeout=DateTimeOffset.UtcNow.AddSeconds(5);while(!condition()){if(DateTimeOffset.UtcNow>timeout)throw new TimeoutException("ACK processing timed out");await Task.Delay(10);}}
    private static void Require(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
}
