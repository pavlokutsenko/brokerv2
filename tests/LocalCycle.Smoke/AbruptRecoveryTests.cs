using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Windows.Storage;

internal static class AbruptRecoveryTests
{
    private static CollectorProfile Profile()=>new(){Name="POWERLOSS-SMOKE",ServerUrl="http://127.0.0.1:1"};
    public static async Task Child(string root)
    {
        var at=DateTimeOffset.UtcNow;var store=new LocalCycleStore(Profile(),root);
        store.BeginSession("old-PID:9001");
        var points=new[]{new RadarPoint(7,"ReadShop",1,100,100,0,true,at),new RadarPoint(8,"PendingShop",8,200,200,0,true,at)};
        store.Observe(new(){Traders=points,LivePlayerPositionAvailable=true},new(),new(0,0,500),at.AddMinutes(-1));
        store.BeginPass(0,0,new());var target=store.Target("READSHOP")!;
        store.Commit(target,new(){SnapshotId="powerloss-stable-snapshot",ReadStartedAtUtc=DateTimeOffset.UtcNow,CapturedAtUtc=DateTimeOffset.UtcNow,
            Precision="wire_int64",Side="sell",Rows=[new(){RowIndex=0,ItemId=57,ItemObjectId=77,Quantity=9,Price=922337203685477580,BasePrice=1,EnchantLevel=16}]});
        DurableJsonFile.Write(Path.Combine(root,"powerloss-child-ready.json"),new{pid=Environment.ProcessId},keepBackup:false);
        await Task.Delay(Timeout.InfiniteTimeSpan); // Parent kills the process without graceful disposal.
    }
    public static async Task Run(string root)
    {
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        if(Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(AbruptRecoveryTests).Assembly.Location);
        start.ArgumentList.Add("--powerloss-child");start.ArgumentList.Add(root);
        using var child=Process.Start(start)!;
        var marker=Path.Combine(root,"powerloss-child-ready.json");var limit=DateTimeOffset.UtcNow.AddSeconds(15);
        while(!File.Exists(marker)&&DateTimeOffset.UtcNow<limit&&!child.HasExited)await Task.Delay(25);
        if(!File.Exists(marker)){if(!child.HasExited)child.Kill();throw new Exception("Power-loss child did not durably commit");}
        child.Kill(entireProcessTree:true);await child.WaitForExitAsync();
        using var recovered=new LocalCycleStore(Profile(),root);
        var pending=recovered.PendingOperations();
        Require(pending.Any(row=>row.Id=="powerloss-stable-snapshot"&&row.Payload.Contains("922337203685477580")),"forced process termination retains stable exact result and outbox");
        recovered.BeginSession("fresh-PID:9002");recovered.BeginPass(0,0,new());
        Require(recovered.Target("READSHOP") is null,"successful undelivered shop is not reread after abrupt termination");
        var target=recovered.Target("PENDINGSHOP")!;
        Require(target.ObjectId==0&&target.RebindOnRead,"new session never uses old client ObjectID for action");
        Require(recovered.NextTargets().Single().TraderKey=="PENDINGSHOP","crashed route is rebuilt from unresolved needs without permanently occupied targets");
        Console.WriteLine("ABRUPT RECOVERY PASS 4 checks");
    }
    private static void Require(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
}

