using PriceCheck.Collector.Models;
using PriceCheck.Contracts;

namespace PriceCheck.Launching;

// Application workflow: reader drain -> owned client stop -> login -> reader restore.
// Collection stays behind callbacks; the launcher has no reader implementation dependency.
public sealed class CharacterRotationService
{
    private readonly LaunchModule _launcher;
    private readonly HashSet<Guid> _busy=[];
    private readonly HashSet<Guid> _failed=[];
    private readonly Dictionary<Guid,ClientSession> _ready=[];
    public CharacterRotationSchedule Schedule { get; }
    public CharacterRotationService(LaunchModule launcher,Func<double>? random=null)
    { _launcher=launcher;Schedule=new(random); }

    public void Started(CollectorProfile profile,ClientSession session,DateTimeOffset now)
    {
        _failed.Remove(profile.Id);Schedule.Forget(profile.Id);
        if(profile.CharacterRotationEnabled) { _ready[profile.Id]=session;Schedule.Observe(profile,session,now); }
        else _ready.Remove(profile.Id);
    }
    public void Forget(Guid profileId) { _failed.Remove(profileId);_ready.Remove(profileId);Schedule.Forget(profileId); }
    public string Status(CollectorProfile profile,ClientSession? session,DateTimeOffset now)
    {
        if(!profile.CharacterRotationEnabled) { Forget(profile.Id);return "Character rotation is off"; }
        if(_failed.Contains(profile.Id)) return "Character change failed · restart the client to retry";
        if(session is null) return "Starts with character 0 on the next launch";
        if(!_launcher.Owns(profile.Id,session)) return "Launch this profile here to enable character rotation";
        if(!_ready.TryGetValue(profile.Id,out var ready) || ready!=session) return "Preparing character 0 and reading the available slots…";
        var due=Schedule.Observe(profile,session,now);
        var remaining=due is null ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0,(due.Value-now).TotalSeconds)));
        return due is null ? "Only character 0 · no rotation needed" :
            $"Account {profile.RotationAccountIndex+1}/{profile.RotationAccountCount} · Character {profile.CharacterSlot+1} of {profile.RotationCharacterCount} · switch in {(int)remaining.TotalHours:00}:{remaining:mm\\:ss} · at {due.Value.ToLocalTime():HH:mm:ss}";
    }
    public bool IsDue(CollectorProfile profile,ClientSession session,DateTimeOffset now)
    {
        if(!profile.CharacterRotationEnabled || _failed.Contains(profile.Id) || _busy.Contains(profile.Id) || !_launcher.Owns(profile.Id,session)) return false;
        CharacterRotationSchedule.Validate(profile);
        return !_ready.TryGetValue(profile.Id,out var ready) || ready!=session || Schedule.IsDue(profile,session,now);
    }

    public async Task<ClientSession> RotateAsync(CollectorProfile profile,ClientSession session,LaunchTemplate? template,
        Func<Task> drain,Func<ClientSession,Task> restore,Action<string> progress,CancellationToken cancellationToken,
        Func<ClientSession,CancellationToken,Task>? beforeLogin=null)
    {
        CharacterRotationSchedule.Validate(profile);
        PriceCheck.Collector.Services.ClientLoginService.Validate(profile);
        if(!profile.CharacterRotationEnabled || !_launcher.Owns(profile.Id,session))
            throw new InvalidOperationException("Character rotation requires a client owned by this profile.");
        if(!_busy.Add(profile.Id)) throw new InvalidOperationException("Character change is already running.");
        try
        {
            progress("Finishing reader work before character change…");
            await drain();
            cancellationToken.ThrowIfCancellationRequested();
            if(!profile.CharacterRotationEnabled) throw new OperationCanceledException("Character rotation was disabled.");
            CharacterRotationSchedule.Validate(profile);
            PriceCheck.Collector.Services.ClientLoginService.Validate(profile);
            var next=_ready.TryGetValue(profile.Id,out var ready) && ready==session ?
                Schedule.NextTarget(profile) : new CharacterRotationSchedule.Target(0,0);
            var previousAccount=profile.RotationAccountIndex;var previousSlot=profile.CharacterSlot;
            try
            {
                profile.RotationAccountIndex=next.AccountIndex;profile.CharacterSlot=next.CharacterSlot;
                PriceCheck.Collector.Services.ClientLoginService.Validate(profile);
            }
            finally { profile.RotationAccountIndex=previousAccount;profile.CharacterSlot=previousSlot; }
            progress($"Closing client · selecting account {next.AccountIndex+1}, character {next.CharacterSlot}…");
            await _launcher.StopAndWaitAsync(profile.Id,session,cancellationToken);
            profile.RotationAccountIndex=next.AccountIndex;profile.CharacterSlot=next.CharacterSlot;
            var replacement=await _launcher.LaunchAsync(profile,template,progress,cancellationToken,beforeLogin);
            await restore(replacement);
            Started(profile,replacement,DateTimeOffset.UtcNow);
            return replacement;
        }
        catch { _failed.Add(profile.Id);Schedule.Forget(profile.Id);throw; }
        finally { _busy.Remove(profile.Id); }
    }
}
