using PriceCheck.Contracts;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Windows;

namespace PriceCheck.Launching;

public sealed record ClientRecoveryRequest(ClientSession Session,bool Reader,bool Collection,string Reason);

// Profile intent survives automatic reader failure; an explicit Stop forgets it.
public sealed class ClientRecoveryService(LaunchModule launcher)
{
    private sealed class State(ClientSession session)
    {
        public ClientSession Session=session;
        public bool Reader,Collection,SeenConnection;
        public DateTimeOffset? Unresponsive,Disconnected,MissingWorld,Healthy;
        public DateTimeOffset RetryAt;
        public int Attempts;
    }
    private readonly Dictionary<Guid,State> _states=[];
    private readonly HashSet<Guid> _busy=[];
    public void Forget(Guid id) => _states.Remove(id);
    public void Arm(CollectorProfile profile,ClientSession session,bool reader,bool collection)
    {
        if(profile.AutoRestartEnabled && _states.TryGetValue(profile.Id,out var s) && s.Session==session)
        { s.Reader=reader;s.Collection=collection;s.RetryAt=DateTimeOffset.UtcNow.AddSeconds(15); }
    }
    public bool Pending(Guid id,ClientSession session) => _states.TryGetValue(id,out var s) && s.Session==session && s.RetryAt!=default;
    public ClientRecoveryRequest? Observe(CollectorProfile profile,ClientSession? session,
        bool reader,bool collection,ClientHealth health,bool? liveWorld,string? fault,DateTimeOffset now)
    {
        if(!profile.AutoRestartEnabled || !profile.AutoLoginEnabled || session is null ||
            (!launcher.Owns(profile.Id,session) && !Pending(profile.Id,session)))
        { Forget(profile.Id);return null; }
        if(!_states.TryGetValue(profile.Id,out var s)) _states[profile.Id]=s=new(session);
        if(s.Session!=session)
        {
            s.Session=session;s.SeenConnection=false;
            s.Unresponsive=s.Disconnected=s.MissingWorld=s.Healthy=null;
        }
        if(fault is null && health.Current) { s.Reader=reader;s.Collection=collection; }
        if(health.Connected==true && (!reader || liveWorld==true)) s.SeenConnection=true;
        s.Unresponsive=health.Current && !health.Responsive ? s.Unresponsive??now : null;
        s.Disconnected=s.SeenConnection && health.Connected==false ? s.Disconnected??now : null;
        s.MissingWorld=reader && liveWorld==false ? s.MissingWorld??now : null;
        string? reason=!health.Current?"Client exited or crashed":health.FatalWindow?"Fatal error window":fault;
        if(reason is null && s.Disconnected is { } disconnected && now-disconnected>=TimeSpan.FromSeconds(20)) reason="Game connection closed";
        if(reason is null && s.Unresponsive is { } stuck && now-stuck>=TimeSpan.FromSeconds(60)) reason="Client has not responded for 60 seconds";
        if(reason is null && s.MissingWorld is { } missing && now-missing>=TimeSpan.FromSeconds(90)) reason="Player world unavailable for 90 seconds";
        if(reason is null)
        {
            s.Healthy??=now;
            if(now-s.Healthy>=TimeSpan.FromMinutes(2)) s.Attempts=0;
            return null;
        }
        s.Healthy=null;
        return now>=s.RetryAt && !_busy.Contains(profile.Id)?new(session,s.Reader,s.Collection,reason):null;
    }

    public async Task<ClientSession> RestartAsync(CollectorProfile profile,ClientRecoveryRequest request,LaunchTemplate? template,
        Func<Task> drain,Func<ClientSession,Task> restore,Action<string> progress,CancellationToken token,
        Func<ClientSession,CancellationToken,Task>? beforeLogin=null)
    {
        ClientLoginService.Validate(profile);
        if(!profile.AutoRestartEnabled || (!launcher.Owns(profile.Id,request.Session) && !Pending(profile.Id,request.Session)))
            throw new InvalidOperationException("Recovery requires the profile's own client.");
        if(!_busy.Add(profile.Id)) throw new InvalidOperationException("Client recovery is already running.");
        try
        {
            progress($"Restarting client: {request.Reason}");
            var draining=drain();
            try { await draining.WaitAsync(TimeSpan.FromSeconds(30),token); }
            catch(Exception e) when(e is not OperationCanceledException)
            {
                // Destroy a dead/stuck owned client before abandoning its hooks.
                // The old collection job observes that exact session's exit.
                ClientLoginService.Validate(profile);
                progress($"Reader drain failed: {e.GetBaseException().Message}; closing the owned client");
                if(launcher.Owns(profile.Id,request.Session)) await launcher.StopAndWaitAsync(profile.Id,request.Session,token);
                try { await draining.WaitAsync(TimeSpan.FromSeconds(30),token); }
                catch(Exception cleanup) when(draining.IsCompleted && cleanup is not OperationCanceledException) { }
                await drain().WaitAsync(TimeSpan.FromSeconds(30),token);
            }
            token.ThrowIfCancellationRequested();ClientLoginService.Validate(profile);
            if(launcher.Owns(profile.Id,request.Session)) await launcher.StopAndWaitAsync(profile.Id,request.Session,token);
            var replacement=await launcher.LaunchAsync(profile,template,progress,token,beforeLogin);
            await restore(replacement);
            var s=_states[profile.Id];s.Session=replacement;s.RetryAt=default;
            s.Unresponsive=s.Disconnected=s.MissingWorld=s.Healthy=null;s.SeenConnection=false;
            return replacement;
        }
        catch
        {
            if(_states.TryGetValue(profile.Id,out var s))
            { s.Attempts++;s.RetryAt=DateTimeOffset.UtcNow.AddSeconds(Math.Min(300,15*Math.Pow(2,Math.Min(s.Attempts-1,5)))); }
            throw;
        }
        finally { _busy.Remove(profile.Id); }
    }
}
