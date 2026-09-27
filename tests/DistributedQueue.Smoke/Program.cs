using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

// Only synthetic traders on a separately launched, dedicated test database API.
if(args.Length!=1 || args[0]!="http://127.0.0.1:3022") throw new ArgumentException("Use the isolated test API on port 3022.");
var profile=new CollectorProfile {Name="Carmine",City="Gludio",ServerUrl=args[0]};
var directory=Path.Combine(Path.GetTempPath(),"PriceCheck-distributed-test",Guid.NewGuid().ToString("N"));
var outbox=new ServerUploadOutbox(()=>{},directory);
var client=new MarketTaskClient();var owner=$"test:{Guid.NewGuid():N}";var name=$"FIXTURE-{Guid.NewGuid():N}";
var inventory=new BrokerInventoryFile {Complete=false,StartedAtUtc=DateTimeOffset.UtcNow,CapturedAtUtc=DateTimeOffset.UtcNow,
    Rows=[new(){TraderName=name,TraderObjectId=123,StoreType=1,ItemId=100,Amount=4}]};
var radar=new RadarSnapshot {Traders=[new(123,name,1,100,200,0,true,DateTimeOffset.UtcNow)]};
var batch=await outbox.EnqueueCycleBrokerAsync(profile,inventory,radar,owner);
var request=Guid.NewGuid().ToString();var keys=new[]{CycleQueue.Key(name)};
Check(!(await client.ClaimAsync(profile,owner,batch,request,keys,0,0)).BrokerAccepted,"claim waits for background broker upload");
using var cancel=new CancellationTokenSource();
var upload=ServerUploadWorker.RunAsync(directory,cancel.Token);
try
{
    MarketTaskClaim claim=new(false,[]);
    for(var i=0;i<100 && claim.Jobs.Count==0;i++)
    { await Task.Delay(100);claim=await client.ClaimAsync(profile,owner,batch,request,keys,0,0); }
    Check(claim.Jobs.Count==1,"C# HTTP client receives server job");var job=claim.Jobs.Single();
    var binding=new CycleBindings();binding.Apply(inventory,radar);
    var target=binding.Bind(job)!;Check(target.ObjectId==123,"local PID binding supplies the action ObjectID");
    Check((await client.RenewAsync(profile,owner,[job])).Single()==job.TraderId,"lease renewal");
    // Another computer sees a different ObjectID but the same durable identity.
    var second=$"test:{Guid.NewGuid():N}";
    var otherBatch=await outbox.EnqueueCycleBrokerAsync(profile,inventory,radar,second);
    for(var i=0;i<100;i++)
    {
        var other=await client.ClaimAsync(profile,second,otherBatch,Guid.NewGuid().ToString(),keys,0,0);
        if(other.BrokerAccepted) {Check(other.Jobs.Count==0,"second PC cannot claim the same trader");break;}
        await Task.Delay(100);
    }
    await outbox.EnqueueCyclePriceAsync(profile,target,new(){CapturedAtUtc=DateTimeOffset.UtcNow,SnapshotId=Guid.NewGuid().ToString(),
        Precision="wire_int64",Side="sell",Rows=[new(){ItemId=100,ItemObjectId=777,Quantity=4,EnchantLevel=7,Price=9007199254740993}]},owner);
    for(var i=0;i<100 && Directory.GetFiles(directory,"*.ready").Length+Directory.GetFiles(directory,"*.sending.*").Length>0;i++) await Task.Delay(100);
    Check(!Directory.Exists(Path.Combine(directory,"rejected")),"all real HTTP envelopes accepted");
    Check(!(await client.RenewAsync(profile,owner,[job])).Any(),"price upload atomically completes the lease");
    Check((await client.ClaimAsync(profile,second,otherBatch,Guid.NewGuid().ToString(),keys,0,0)).Jobs.Count==0,"24h persists for another computer");
    var state=await client.StateAsync(profile);Check(state.Checked>0,"UI receives server confirmation");
    Console.WriteLine("DISTRIBUTED_HTTP_OK broker_ack concurrent_owners local_binding durable_price_upload exact_int64 shared_24h");
}
finally {cancel.Cancel();try {await upload;}catch(OperationCanceledException) {}}
static void Check(bool value,string message) {if(!value)throw new Exception(message);}
