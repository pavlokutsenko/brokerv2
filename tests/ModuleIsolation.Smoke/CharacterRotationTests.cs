using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Launching;

internal static class CharacterRotationTests
{
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static async Task Run()
    {
        var profile=new CollectorProfile {CharacterRotationEnabled=true,AutoLoginEnabled=true,
            LoginName="fixture",LoginPassword="fixture",RotationCharacterCount=3};
        var at=DateTimeOffset.UtcNow;var session=new PriceCheck.Contracts.ClientSession(1,at);
        Check(new CharacterRotationSchedule(()=>0).Observe(profile,session,at)==at.AddMinutes(45),"Default lower jitter boundary is 45 minutes.");
        Check(new CharacterRotationSchedule(()=>1).Observe(profile,session,at)==at.AddMinutes(75),"Default upper jitter boundary is 75 minutes.");
        var clock=new CharacterRotationSchedule(()=>.5);var due=clock.Observe(profile,session,at);
        profile.RotationCharacterSlots=[0,3,6];
        profile.CharacterSlot=0;Check(clock.NextSlot(profile)==3,"Rotation uses the observed occupied list, including sparse slots.");
        profile.CharacterSlot=6;Check(clock.NextSlot(profile)==0,"Observed occupied slots wrap to the first entry.");
        profile.RotationCharacterSlots=[];profile.CharacterSlot=0;
        Check(clock.Observe(profile,session,at.AddMinutes(10))==due && !clock.IsDue(profile,session,at.AddMinutes(59)),
            "Polling cannot redraw or slide the interval.");
        Check(clock.IsDue(profile,session,at.AddHours(1)),"Due time triggers rotation.");
        profile.RotationCharacterCount=1;
        Check(clock.Observe(profile,session,at) is null,"One actual character does not restart needlessly.");
        profile.RotationCharacterCount=3;
        var restored=JsonSerializer.Deserialize<CollectorProfile>(JsonSerializer.Serialize(profile))!;
        Check(restored.RotationIntervalMinutes==60 && restored.RotationJitterMinutes==15 && restored.CharacterRotationEnabled && restored.RotationCharacterCount==0,
            "Persist configuration but rediscover the character roster every login.");

        var processes=new FakeProcesses();var selected=new List<int>();
        var pauses=0;
        var launcher=new LaunchModule(processes,(_,p,_)=>{selected.Add(p.CharacterSlot);p.RotationCharacterCount=3;return Task.CompletedTask;},processes.Identity,
            _=>{pauses++;return Task.CompletedTask;});
        var rotation=new CharacterRotationService(launcher,()=>.5);
        var live=await launcher.LaunchAsync(profile,null,_=>{},CancellationToken.None);
        rotation.Started(profile,live,at);
        var drain=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumed=new List<int>();
        var changing=rotation.RotateAsync(profile,live,null,()=>drain.Task,s=>{resumed.Add(s.ProcessId);return Task.CompletedTask;},_=>{},CancellationToken.None);
        Check(!changing.IsCompleted && processes.Terminations==0,"Do not close the client before reader drain.");
        try { await rotation.RotateAsync(profile,live,null,()=>Task.CompletedTask,_=>Task.CompletedTask,_=>{},CancellationToken.None);throw new Exception("Duplicate change accepted"); }
        catch(InvalidOperationException) { }
        drain.SetResult();live=await changing;
        for(var i=0;i<2;i++) live=await rotation.RotateAsync(profile,live,null,()=>Task.CompletedTask,_=>Task.CompletedTask,_=>{},CancellationToken.None);
        Check(selected.SequenceEqual(new[]{0,1,2,0}) && processes.Terminations==3 && resumed.Count==1 && pauses==3,
            "Occupied roster rotates 0→1→2→0 with one stop/login and one requested reader restore per change.");
        profile.CharacterRotationEnabled=false;rotation.Status(profile,live,at);
        profile.CharacterRotationEnabled=true;profile.CharacterSlot=2;
        Check(rotation.IsDue(profile,live,at),"Enabling rotation on a running owned client prepares the first character immediately.");
        live=await rotation.RotateAsync(profile,live,null,()=>Task.CompletedTask,_=>Task.CompletedTask,_=>{},CancellationToken.None);
        Check(profile.CharacterSlot==0,"A fresh rotation starts at zero instead of continuing a previous manual slot.");
        var stops=processes.Terminations;
        try { await rotation.RotateAsync(profile,live,null,()=>Task.FromException(new IOException("Drain failed")),_=>Task.CompletedTask,_=>{},CancellationToken.None); }
        catch(IOException) { }
        Check(processes.Terminations==stops && !rotation.IsDue(profile,live,at.AddDays(1)),"Failed drain preserves client and latches the error instead of retrying forever.");
        launcher.Stop(profile.Id);
        Console.WriteLine("CHARACTER_ROTATION_OK jitter stable_deadline roster_refresh circular_slots drain_before_stop failure_latch");
    }
}
