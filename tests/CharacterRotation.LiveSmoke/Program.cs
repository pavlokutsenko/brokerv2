using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Collector.Runtime.Radar;
using PriceCheck.Launching;
using PriceCheck.Windows;

var profile=(await new ProfileStore().LoadAsync()).Single(p=>p.Id==Guid.Parse("6a0358a7-984d-4ef1-8570-283b79c3cf88"));
var template=(await new LaunchTemplateStore().LoadAsync()).SingleOrDefault(t=>t.Id==profile.LaunchTemplateId);
profile.CharacterRotationEnabled=true;profile.CharacterSlot=0;profile.CollectionEnabled=false;
var launcher=new LaunchModule();var rotation=new CharacterRotationService(launcher);
await using var radar=new RadarSessionManager();
using var stop=new CancellationTokenSource(TimeSpan.FromMinutes(9));
async Task VerifyWorld(PriceCheck.Contracts.ClientSession session)
{
    await radar.StartAsync(session.ProcessId,null,false,stop.Token);
    var deadline=DateTimeOffset.UtcNow.AddSeconds(55);
    while(DateTimeOffset.UtcNow<deadline)
    {
        var snapshot=radar.Snapshot(session.ProcessId,null,false);
        if(snapshot is not null && Math.Abs(snapshot.PlayerX)>100 && Math.Abs(snapshot.PlayerY)>100)
        {
            Console.WriteLine($"ROTATION_WORLD slot={profile.CharacterSlot} count={profile.RotationCharacterCount} pid={session.ProcessId} x={snapshot.PlayerX:F0} y={snapshot.PlayerY:F0}");
            return;
        }
        await Task.Delay(300,stop.Token);
    }
    throw new TimeoutException("Selected character did not enter the world.");
}
try
{
    var session=await launcher.LaunchAsync(profile,template,Console.WriteLine,stop.Token);
    rotation.Started(profile,session,DateTimeOffset.UtcNow);
    await VerifyWorld(session);
    Console.WriteLine(rotation.Status(profile,session,DateTimeOffset.UtcNow));
    var count=profile.RotationCharacterCount;
    if(count>1)
    {
        // Exercise every real slot and the wrap to 0; no market worker, movement or shop requests.
        for(var i=0;i<count;i++)
        {
            var old=session;
            session=await rotation.RotateAsync(profile,old,template,()=>radar.StopAsync(old.ProcessId),VerifyWorld,Console.WriteLine,stop.Token);
            if(ClientProcessIdentity.IsCurrent(old) || profile.CharacterSlot!=(i+1)%count)
                throw new InvalidOperationException("Old process survived or circular slot order was wrong.");
        }
    }
    await radar.StopAsync(session.ProcessId);
    Console.WriteLine($"CHARACTER_ROTATION_LIVE_OK count={count} collection=false");
}
finally { launcher.StopOwnedClients(); }
