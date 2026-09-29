using PriceCheck.Collector.Models;
using PriceCheck.Collector.Runtime.Radar;
using PriceCheck.Collector.Services;
using PriceCheck.Launching;

internal static class BrokerIdentityTests
{
    private static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    public static async Task Run()
    {
        var cache=new RadarIdentityCache();
        for(var id=1;id<=77;id++)
        {
            cache.Apply(new CharacterPacket(id,$"Shop{id}","",new[]{1,3,8}[id%3],80000+id,148000,0));
            cache.Apply(new DeletePacket(id));
        }
        cache.Apply(new CharacterPacket(999,"OutsideBroker","",1,1,1,0));
        var wanted=Enumerable.Range(1,77).Select(id=>(long)id).ToHashSet();
        var current=new RadarSnapshot {ProcessId=42,BrokerIdentities=cache.Snapshot()};
        var result=BrokerIdentityJoin.Resolve(42,wanted,new(){ProcessId=42},current,[]);
        Check(result.Length==77 && result.All(t=>!t.IsVisible),
            "Names received before collection survive knownlist deletion and absent native actors; only broker IDs are admitted.");
        Check(BrokerIdentityJoin.Resolve(43,wanted,new(){ProcessId=43},current,[]).Length==0,
            "Previous process identities must never name the current broker.");
        cache.Apply(new CharacterPacket(1,"Shop1","",0,80001,148000,0));
        result=BrokerIdentityJoin.Resolve(42,wanted,current,new(){ProcessId=42,BrokerIdentities=cache.Snapshot()},[]);
        Check(result.Single(t=>t.ObjectId==1).KioskType==0,"Explicit closure wins over an older open-shop packet.");
        Check(new RadarIdentityCache().Snapshot().Count==0,"A replacement reader starts with an empty identity cache.");

        var processes=new FakeProcesses();var events=new List<string>();
        var launcher=new LaunchModule(processes,(_,_,_)=>{events.Add("login");return Task.CompletedTask;},processes.Identity,_=>Task.CompletedTask);
        var profile=new CollectorProfile {LoginServerId=1,AutoLoginEnabled=true,CharacterRotationEnabled=true,
            RotationCharacterCount=2,LoginName="fixture",LoginPassword="fixture"};
        var live=await launcher.LaunchAsync(profile,null,_=>{},CancellationToken.None,
            (_,_)=>{events.Add("reader");return Task.CompletedTask;});
        Check(events.SequenceEqual(new[]{"reader","login"}),"Reader must be active before world-entry packets arrive.");
        var rotation=new CharacterRotationService(launcher);rotation.Started(profile,live,DateTimeOffset.UtcNow);
        events.Clear();
        live=await rotation.RotateAsync(profile,live,null,()=>{events.Add("drain");return Task.CompletedTask;},
            _=>{events.Add("restore");return Task.CompletedTask;},_=>{},CancellationToken.None,
            (_,_)=>{events.Add("reader");return Task.CompletedTask;});
        Check(events.SequenceEqual(new[]{"drain","reader","login","restore"}),"Rotation captures new identities before login and resumes afterwards.");
        launcher.Stop(profile.Id);
        var stops=processes.Terminations;
        try { await launcher.LaunchAsync(profile,null,_=>{},CancellationToken.None,(_,_)=>Task.FromException(new IOException("attach failed"))); }
        catch(IOException) { }
        Check(processes.Terminations==stops+1,"Failed early attach closes its owned client without entering the world.");
        Console.WriteLine("BROKER_IDENTITIES_OK missing_77 packet_retention same_pid broker_only early_login rotation failure_cleanup");
    }
}
