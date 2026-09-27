using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class ProtocolTests
{
    public static async Task Run(string root)
    {
        var portProbe=new TcpListener(IPAddress.Loopback,0);portProbe.Start();var port=((IPEndPoint)portProbe.LocalEndpoint).Port;portProbe.Stop();
        using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
        using var stop=new CancellationTokenSource();
        var received=new ConcurrentQueue<string>();var priceAccepted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstBroker=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var releaseBroker=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRead=DateTimeOffset.UtcNow.AddHours(-1);
        var server=Task.Run(async()=>{
            while(!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try{context=await listener.GetContextAsync().WaitAsync(stop.Token);}catch(OperationCanceledException){break;}
                using var reader=new StreamReader(context.Request.InputStream);var request=JsonDocument.Parse(await reader.ReadToEndAsync());
                var kind=context.Request.Url!.AbsolutePath.Split('/').Last();received.Enqueue(kind);
                if(kind=="broker"&&!firstBroker.Task.IsCompleted){firstBroker.SetResult();await releaseBroker.Task;}
                var response=kind=="history"?JsonSerializer.Serialize(new{traders=new[]{new{traderKey="SHOP",lastReadAtUtc=oldRead,
                    verificationRequiredAtUtc=(DateTimeOffset?)null,verificationRevision="remote-opaque-token",checkedX=100,checkedY=100,lastReadKioskType=1}}}):"{\"accepted\":true,\"current\":true}";
                var bytes=Encoding.UTF8.GetBytes(response);context.Response.ContentType="application/json";context.Response.ContentLength64=bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);context.Response.Close();
                if(kind=="price")priceAccepted.TrySetResult();
            }
        });
        var time=DateTimeOffset.UtcNow;
        var profile=new CollectorProfile{Name="HTTP-SMOKE",ServerUrl=$"http://127.0.0.1:{port}"};
        var boundary=new CycleRadarPool();var center=new MarketZone(0,0,500);
        RadarPoint Shop()=>new(7,"Shop",1,100,100,0,true,time){StateObservedAtUtc=time};
        RadarSnapshot Radar()=>new(){ProcessId=11,LivePlayerPositionAvailable=true,Traders=[Shop()],CapturedAtUtc=time};
        using(var store=new LocalCycleStore(profile,root,()=>time))
        {
            store.BeginSession("HTTP");store.Observe(Radar(),boundary,center,time.AddMinutes(-1));
            await store.ReconcileAsync();
            Require(store.Target("SHOP") is null,"initial history reconciliation skips recent legacy exact read");
            time=time.AddMilliseconds(1);store.Observe(Radar(),boundary,center,time.AddMinutes(-1));
            Require(store.Target("SHOP") is null,"server checked coordinates do not cause false movement");
            store.SetRecheckHours(0.5);var adopted=store.Target("SHOP")!;
            Require(adopted.VerificationRevision=="remote-opaque-token","fresh PC adopts server opaque verification token");
            time=time.AddMilliseconds(1);store.ObserveClosed(Shop() with{KioskType=0},center,time.AddMinutes(-1),false);
            time=time.AddMilliseconds(1);store.Observe(withReopen(),boundary,center,time.AddMinutes(-1));
            var reopened=store.Target("SHOP")!;await store.ReconcileAsync();
            Require(store.Target("SHOP")?.VerificationRevision==reopened.VerificationRevision,"old server history cannot clear pending local reopen");
            store.SetRecheckHours(24);
            var traderPoints=Enumerable.Range(0,60).Select(i=>new RadarPoint(100+i,"B"+i,1,200+i,200,0,true,time)).ToArray();
            store.Observe(new(){Traders=traderPoints,LivePlayerPositionAvailable=true},boundary,center,time.AddMinutes(-1));
            store.ApplyBroker(new(){StartedAtUtc=time.AddMinutes(-1),CapturedAtUtc=time,Complete=true,Rows=traderPoints.Select(p=>new BrokerInventoryRow{TraderName=p.Name,TraderObjectId=p.ObjectId,StoreType=1,ItemId=57,Amount=1}).ToArray()},new(){Traders=traderPoints},"http-broker");
            await firstBroker.Task.WaitAsync(TimeSpan.FromSeconds(5));
            time=time.AddMilliseconds(1);store.Commit(reopened,new(){SnapshotId="http-individual",Precision="wire_int64",ReadStartedAtUtc=time,CapturedAtUtc=time,Side="sell",
                Rows=[new(){ItemId=57,ItemObjectId=999,Quantity=1,Price=12345678901234,BasePrice=1}]});
            releaseBroker.SetResult();
            await priceAccepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var order=received.ToArray();var firstIndex=Array.IndexOf(order,"broker");
            Require(Array.IndexOf(order,"price")==firstIndex+1,"individual exact price bypasses unrelated broker backlog");
            Require(store.Target("SHOP") is null,"background acknowledgement never needed to skip successful shop");
        }
        stop.Cancel();listener.Stop();await server;
        Console.WriteLine("HTTP PROTOCOL PASS 6 checks");
        RadarSnapshot withReopen()=>new(){ProcessId=11,LivePlayerPositionAvailable=true,Traders=[Shop() with{LastReopenedAtUtc=time}],CapturedAtUtc=time};
    }
    private static void Require(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
}

