using PriceCheck.Collector.Models;
using PriceCheck.Contracts;

namespace PriceCheck.Launching;

// Runtime clock only; configuration is persisted with the collector profile.
public sealed class CharacterRotationSchedule
{
    private readonly Func<double> _random;
    private readonly Dictionary<Guid, Entry> _entries=[];
    private sealed record Entry(ClientSession Session, int Count, int Minutes, int Jitter, DateTimeOffset? Due);
    public CharacterRotationSchedule(Func<double>? random=null) => _random=random ?? Random.Shared.NextDouble;

    public static void Validate(CollectorProfile profile)
    {
        if(!profile.CharacterRotationEnabled) return;
        if(!profile.AutoLoginEnabled) throw new InvalidOperationException("Character rotation requires auto login.");
        if(profile.RotationIntervalMinutes is <1 or >1440 || profile.RotationJitterMinutes<0 ||
            profile.RotationJitterMinutes>=profile.RotationIntervalMinutes)
            throw new InvalidOperationException("Rotation interval must be 1–1440 minutes; random offset must be smaller than the interval.");
    }

    public DateTimeOffset? Observe(CollectorProfile profile, ClientSession session, DateTimeOffset now)
    {
        if(!profile.CharacterRotationEnabled) { Forget(profile.Id); return null; }
        Validate(profile);
        if(profile.RotationCharacterCount is <1 or >7) return null; // Native login has not published the roster yet.
        if(_entries.TryGetValue(profile.Id,out var entry) && entry.Session==session &&
            entry.Count==profile.RotationCharacterCount && entry.Minutes==profile.RotationIntervalMinutes &&
            entry.Jitter==profile.RotationJitterMinutes) return entry.Due;
        var minutes=profile.RotationIntervalMinutes+(2*Math.Clamp(_random(),0,1)-1)*profile.RotationJitterMinutes;
        DateTimeOffset? due=profile.RotationCharacterCount>1 ? now.AddMinutes(minutes) : null;
        _entries[profile.Id]=new(session,profile.RotationCharacterCount,profile.RotationIntervalMinutes,profile.RotationJitterMinutes,due);
        return due;
    }

    public bool IsDue(CollectorProfile profile,ClientSession session,DateTimeOffset now)=>
        Observe(profile,session,now) is { } due && now>=due;
    public int NextSlot(CollectorProfile profile)
    {
        var slots = profile.RotationCharacterSlots.Length > 0 ? profile.RotationCharacterSlots :
            Enumerable.Range(0, profile.RotationCharacterCount).ToArray();
        if (slots.Length == 0) throw new InvalidOperationException("The occupied character list has not been read.");
        var index = Array.IndexOf(slots, profile.CharacterSlot);
        return slots[(index + 1) % slots.Length];
    }
    public void Forget(Guid profileId)=>_entries.Remove(profileId);
}
