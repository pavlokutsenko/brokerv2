using System.Diagnostics;
using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

if(args is ["--powerloss-child",var childRoot]){await AbruptRecoveryTests.Child(childRoot);return;}

var root=Path.Combine(Path.GetTempPath(),"PriceCheck-LocalCycle-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var time=DateTimeOffset.UtcNow;
var profile=new CollectorProfile{Name="LOCAL-SMOKE",ServerUrl="http://127.0.0.1:1"};
var boundary=new CycleRadarPool([[-10000,-10000],[10000,-10000],[10000,10000],[-10000,10000]]);
var center=new MarketZone(0,0,500);
var checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
using(var roster=JsonDocument.Parse($$"""
    [{"isActive":true,"kioskType":1,"lastReadAtUtc":"{{time.AddHours(-1):O}}","verificationRequiredAtUtc":"{{time.AddHours(-2):O}}","lastReadKioskType":1},
     {"isActive":true,"kioskType":1,"lastReadAtUtc":"{{time.AddHours(-3):O}}","verificationRequiredAtUtc":"{{time.AddHours(-2):O}}","lastReadKioskType":1},
     {"isActive":false,"kioskType":1,"lastReadAtUtc":"{{time.AddHours(-1):O}}"},
     {"isActive":true,"kioskType":1,"lastReadAtUtc":"{{time.AddHours(-1):O}}","lastReadKioskType":3},
     {"isActive":true,"kioskType":3,"lastReadAtUtc":"{{time.AddHours(-25):O}}"}]
    """))
    Check(LocalCycleStore.CountCurrentServerPrices(roster.RootElement,time,24)==1,
        "server count excludes stale, invalidated, closed and type-changed prices");
MarketPreviewTests.Run(root);
Check(profile.TraderPauseSeconds==30,"trader pause defaults to thirty seconds");
var migratedPause=JsonSerializer.Deserialize<CollectorProfile>("{\"NewTargetIntervalSeconds\":45}")!;
Check(migratedPause.TraderPauseSeconds==45,"old new-target interval migrates to trader pause");
Check(!JsonSerializer.Serialize(migratedPause).Contains("NewTargetIntervalSeconds"),
    "old interval name is not persisted again");
Check(new CollectorProfile{TraderPauseSeconds=0}.TraderPauseSeconds==0,"trader pause can be disabled");
RadarPoint Point(string name="Shop",int oid=7,int type=1,double x=100,double y=100)=>new(oid,name,type,x,y,0,true,time){StateObservedAtUtc=time};
RadarSnapshot Frame(params RadarPoint[] points)=>new(){ProcessId=123,LivePlayerPositionAvailable=true,PlayerX=0,PlayerY=0,Traders=points,CapturedAtUtc=time};
ShopCaptureFile Capture(string id,CycleTarget target)=>new(){SnapshotId=id,Precision="wire_int64",Side=target.KioskType==3?"buy":"sell",
    ReadStartedAtUtc=time,CapturedAtUtc=time,Rows=[new(){RowIndex=0,ItemId=57,ItemObjectId=991,Quantity=7,Price=9007199254740993,EnchantLevel=6,BasePrice=9}]};

using(var store=new LocalCycleStore(profile,root,()=>time))
{
    var centerRadar=Frame(Point("Shared",oid:11));
    store.RecordMarketRadar(centerRadar);
    store.RecordMarketRadar(new RadarSnapshot{CapturedAtUtc=time.AddSeconds(-1)});
    Check(store.MarketRadar?.Traders.Single().Name=="Shared","market radar retains latest complete center snapshot across account turns");
    store.BeginSession("client1");store.Observe(Frame(Point()),boundary,center,time.AddSeconds(-1));
    store.BeginPass(0,0,boundary);var original=store.NextTargets().Single();
    time=time.AddMilliseconds(1);Check(store.Commit(original,Capture("shop-first",original)),"full shop durable commit");
    Check(store.NextTargets().Count==0,"successful pending upload excludes shop from route");
    Check(store.Status(new()).CurrentPriceTraders==1,"current price count includes valid exact read");
    var price=store.PendingOperations().Single(o=>o.Kind=="price");using(var body=JsonDocument.Parse(price.Payload))
    {Check(body.RootElement.GetProperty("rows")[0].GetProperty("price").GetString()=="9007199254740993","int64 exact price roundtrips as decimal string");
     Check(!body.RootElement.TryGetProperty("traders",out _),"one trader per price payload");}
    store.Observe(Frame(Point()),boundary,center,time.AddSeconds(-1));Check(store.Target("SHOP") is null,"unchanged open packet does not requeue");
    time=time.AddSeconds(1);
    store.ApplyBroker(new(){Complete=true,StartedAtUtc=time,CapturedAtUtc=time.AddMilliseconds(1),Rows=[new(){TraderName="Shop",TraderObjectId=7,StoreType=1,ItemId=57,Amount=9}]},Frame(Point()),"normal-next-epoch");
    Check(store.Target("SHOP") is null,"next complete broker with unchanged lowercase wire side does not invalidate price");
    store.ApplyBroker(new(){Complete=false,StartedAtUtc=time.AddMinutes(-1),CapturedAtUtc=time,Rows=[new(){TraderName="Shop",TraderObjectId=7,StoreType=1,ItemId=57,Amount=55}]},Frame(Point()),"partial-epoch");
    Check(store.Target("SHOP") is null,"quantity-only partial broker keeps successful price fresh");
    time=time.AddSeconds(1);var close=Point(type:0) with {StateObservedPlayerX=800,StateObservedPlayerY=0};
    store.ObserveClosed(close,center,time.AddSeconds(-1),true);
    Check(store.Target("SHOP") is null,"outside-center explicit closure cancels approach");
    Check(store.Status(new()).CurrentPriceTraders==0,"closed pending shop is not counted as a current price");
    Check(!store.PendingOperations().Any(o=>o.Payload.Contains("closed_confirmed")),"outside-center closure never removes server offers");
    time=time.AddMilliseconds(1);store.Observe(Frame(Point() with{LastReopenedAtUtc=time}),boundary,center,time.AddSeconds(-1));
    var reopened=store.Target("SHOP")!;Check(reopened.Revision>original.Revision,"same quantity reopen requires exact reread");
    time=time.AddMilliseconds(1);store.Commit(original,Capture("stale-read",original));
    Check(store.Target("SHOP")?.Revision==reopened.Revision,"old result after reopen cannot clear new requirement");
    store.Commit(reopened,Capture("new-read",reopened));Check(store.Target("SHOP") is null,"current generation valid result clears requirement");
    store.BeginSession("client2");store.Observe(Frame(Point(oid:22)),boundary,center,time.AddSeconds(-1));
    store.BeginPass(0,0,boundary);Check(store.NextTargets().Count==0,"new client ObjectID does not make recently read shop new");
    time=time.AddHours(25);Check(store.Target("SHOP") is not null,"24h default interval becomes due");
    Check(store.Status(new()).CurrentPriceTraders==0,"expired exact price is not current");
    store.SetRecheckHours(48);Check(store.Target("SHOP") is null,"interval increase recomputes from actual last read");
    store.SetRecheckHours(1);Check(store.Target("SHOP") is not null,"interval reduction recomputes due without rewriting read time");
    time=time.AddSeconds(1);close=Point(type:0) with{StateObservedPlayerX=900,StateObservedPlayerY=0};
    store.ObserveClosed(close,center,time.AddSeconds(-1),true);
    store.ObserveClosed(close,center,time.AddSeconds(1),true);
    Check(!store.PendingOperations().Any(o=>o.Payload.Contains("closed_confirmed")),"carried older closure after center entry cannot confirm removal");
    time=time.AddSeconds(2);store.ObserveClosed(Point(type:0) with{StateObservedPlayerX=0,StateObservedPlayerY=0},center,time.AddSeconds(-1),true);
    Check(store.PendingOperations().Count(o=>o.Payload.Contains("closed_confirmed"))==1,"fresh central closure emits immediate separate confirmation event");
    var before=store.PendingUploads;
    store.ObserveClosed(Point(type:0) with{StateObservedPlayerX=0,StateObservedPlayerY=0},center,time.AddSeconds(-1),true);
    Check(store.PendingUploads==before,"repeated unchanged central closed observation is idempotent");
    store.Observe(Frame(Point("Far",33,x:3500)),boundary,center,time);
    Check(!store.Keys.Contains("FAR"),"new discovery radius3000 enforced");
    store.Observe(Frame(Point("Outside",44,x:10500)),boundary,center,time);
    Check(!store.Keys.Contains("OUTSIDE"),"approved collection boundary enforced");
    store.ApplyBroker(new(){Complete=true,StartedAtUtc=time,CapturedAtUtc=time,Rows=[new(){TraderName="Outside",TraderObjectId=44,StoreType=1,ItemId=100,Amount=3}]},Frame(Point("Outside",44,x:10500)),"outside-epoch",boundary);
    Check(!store.Keys.Contains("OUTSIDE")&&!store.PendingOperations().Any(o=>o.Id.StartsWith("outside-epoch")),"broker outside boundary is excluded before persistence/outbox");
    var beforeOutsideClose=store.PendingUploads;
    store.ObserveClosed(Point(type:0,x:10500) with {StateObservedPlayerX=0,StateObservedPlayerY=0},center,time.AddSeconds(-1),true);
    Check(store.PendingUploads==beforeOutsideClose,"known shop outside boundary cannot enqueue central pending or confirmed state");
    time=time.AddMilliseconds(1);store.Observe(Frame(Point() with{LastReopenedAtUtc=time}),boundary,center,time.AddSeconds(-1));
    var final=store.Target("SHOP")!;time=time.AddMilliseconds(1);store.Commit(final,Capture("final-shop-read",final));
}
using(var restored=new LocalCycleStore(profile,root,()=>time))
{
    Check(restored.PendingUploads>=4,"SQLite restores each individual undelivered operation");
    restored.BeginSession("client3");restored.SetRecheckHours(48);restored.Observe(Frame(Point(oid:99)),boundary,center,time);
    restored.BeginPass(0,0,boundary);Check(restored.NextTargets().Count==0,"restart preserves successful read history");
}
using(var finite=new LocalCycleStore(new(){Name="FINITE",ServerUrl=profile.ServerUrl},root,()=>time))
{
    finite.BeginSession("f");finite.Observe(Frame(Enumerable.Range(0,100).Select(i=>Point("T"+i,i+1,x:i*10)).ToArray()),boundary,center,time);
    finite.BeginPass(0,0,boundary);Check(finite.NextTargets().Count==20,"detailed path section bounded to20");
    finite.Observe(Frame(Enumerable.Range(100,1000).Select(i=>Point("T"+i,i+1,x:i%2000)).ToArray()),boundary,center,time);
    var visits=0;
    while(finite.NextTargets().FirstOrDefault() is {} t){finite.FinishAttempt(t.TraderKey);visits++;if(visits>500)throw new Exception("infinite pass");}
    Check(visits==164,"continual discoveries have finite admission cap and defer remainder to next natural cycle");
}
Console.WriteLine($"LOCAL CYCLE PASS {checks} checks · {root}");
await ProtocolTests.Run(root);
await ParallelDeliveryTests.Run(root);
BrokerCompactionTests.Run(root);
BrokerDeliveryStatusTests.Run(root);
await AcknowledgementTests.Run(root);
await AbruptRecoveryTests.Run(root);
TailReaderTests.Run(root);
BrokerTargetTests.Run(root);
CenterRadarTests.Run(root);
CenterRadarEvidenceTests.Run();
StorageMaintenanceTests.Run(root);
ServerRosterTests.Run(root);
RouteSectionTests.Run(root);
