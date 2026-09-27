using PriceCheck.Collector.Models;
using PriceCheck.Launching;
using PriceCheck.Windows;

internal static class ClientRecoveryTests
{
    private static void Check(bool ok,string reason) { if(!ok) throw new Exception(reason); }
    public static async Task Run()
    {
        var profile=new CollectorProfile {AutoLoginEnabled=true,AutoRestartEnabled=true,
            LoginName="fixture",LoginPassword="fixture",CharacterSlot=3};
        var processes=new FakeProcesses();var loginFails=false;
        var launcher=new LaunchModule(processes,(_,_,_)=>loginFails?Task.FromException(new IOException("Login disconnected")):Task.CompletedTask,processes.Identity,
            _=>Task.CompletedTask);
        var recovery=new ClientRecoveryService(launcher);
        var session=await launcher.LaunchAsync(profile,null,_=>{},CancellationToken.None);
        var at=DateTimeOffset.UtcNow;
        ClientRecoveryRequest? Observe(ClientHealth health,DateTimeOffset time,string? fault=null,bool? world=true) =>
            recovery.Observe(profile,session,true,true,health,world,fault,time);
        var healthy=new ClientHealth(true,true,false,true);
        Check(Observe(healthy,at) is null,"Healthy idle client must not restart.");
        var lost=new ClientHealth(true,true,false,false);
        Check(Observe(lost,at.AddSeconds(1)) is null && Observe(lost,at.AddSeconds(20)) is null,"Connection loss requires sustained confirmation.");
        var request=Observe(lost,at.AddSeconds(62))!;
        Check(request.Reason=="Game connection closed" && request.Reader && request.Collection,"Confirmed disconnect keeps reader/collection intent.");
        var drain=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumes=0;
        var restarting=recovery.RestartAsync(profile,request,null,()=>drain.Task,_=>{resumes++;return Task.CompletedTask;},_=>{},CancellationToken.None);
        Check(!restarting.IsCompleted && processes.Terminations==0,"Restart waits for reader cleanup.");
        drain.SetResult();session=await restarting;
        Check(resumes==1 && profile.CharacterSlot==3 && !recovery.Pending(profile.Id,session),"Recovery restores same character and immediately allows collection refresh.");
        Observe(healthy,at);
        var exited=new ClientHealth(false,false,false,null);
        processes.Terminate(session.ProcessId);
        request=Observe(exited,at.AddSeconds(1))!;
        Check(request is not null,"Owned crash restarts even though process has already exited.");
        loginFails=true;
        try { await recovery.RestartAsync(profile,request!,null,()=>Task.CompletedTask,_=>Task.CompletedTask,_=>{},CancellationToken.None); }
        catch(IOException) { }
        Check(recovery.Pending(profile.Id,session) && Observe(exited,DateTimeOffset.UtcNow) is null,"Failed login retains recovery ownership and backs off.");
        request=Observe(exited,DateTimeOffset.UtcNow.AddMinutes(6))!;
        loginFails=false;
        session=await recovery.RestartAsync(profile,request,null,()=>Task.CompletedTask,_=>Task.CompletedTask,_=>{},CancellationToken.None);
        Check(!recovery.Pending(profile.Id,session),"Later retry recovers without remaining stuck in pending state.");
        Observe(healthy,at);
        var hanging=new ClientHealth(true,false,false,true);
        Check(Observe(hanging,at.AddSeconds(1)) is null && Observe(hanging,at.AddSeconds(60)) is null,
            "Short loading/native activity is not a hung client.");
        Check(Observe(hanging,at.AddSeconds(182))!.Reason.Contains("180 seconds"),"Sustained unresponsive client is detected.");
        Observe(healthy,at.AddMinutes(2));
        Check(Observe(healthy,at.AddMinutes(3),world:false) is null && Observe(healthy,at.AddMinutes(4),world:false) is null,
            "World transition has a loading grace period.");
        Check(Observe(healthy,at.AddMinutes(9),world:false) is not null,"Long world loss triggers recovery.");
        var drainCalls=0;
        request=Observe(healthy,at.AddMinutes(10),"Reader failure")!;
        session=await recovery.RestartAsync(profile,request,null,()=>++drainCalls==1?Task.FromException(new IOException("Cleanup fault")):Task.CompletedTask,
            _=>Task.CompletedTask,_=>{},CancellationToken.None);
        Check(drainCalls==2,"Failed cleanup closes owned client before retrying teardown.");
        recovery.Forget(profile.Id);launcher.Stop(profile.Id);
        Check(Observe(exited,at.AddDays(1)) is null,"Explicit stop cannot relaunch.");
        session=await launcher.LaunchAsync(profile,null,_=>{},CancellationToken.None);
        Check(recovery.Observe(profile,session,false,false,healthy,null,null,at) is null,"Launcher health requires no reader.");
        Check(recovery.Observe(profile,session,false,false,lost,null,null,at.AddSeconds(1)) is null,"Launcher disconnect has a grace period.");
        var launcherRequest=recovery.Observe(profile,session,false,false,lost,null,null,at.AddSeconds(62));
        Check(launcherRequest is { Reader:false,Collection:false,Reason:"Game connection closed" },"Launcher detects connection loss without collection or world reads.");
        recovery.Forget(profile.Id);launcher.Stop(profile.Id);
        Console.WriteLine("CLIENT_RECOVERY_OK disconnect_grace crash same_slot drain retry_backoff manual_stop");
    }
}
