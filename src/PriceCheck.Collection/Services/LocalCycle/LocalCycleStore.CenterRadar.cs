using System.Security.Cryptography;
using System.Text;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed partial class LocalCycleStore
{
    // A completed stationary center observation can retire old history that
    // never produced a kiosk=0 packet in this client. Roaming snapshots cannot.
    public int ReconcileCenterRadar(RadarSnapshot radar, MarketZone center, DateTimeOffset started,
        string epochId, bool complete, IReadOnlyList<BrokerNativeStateObservation>? nativeStates = null)
    {
        var freshNative=(nativeStates??[]).Where(s=>s.ObservedAt>=started && s.ObservedAt<=_clock().AddSeconds(5) &&
            _clock()-s.ObservedAt<=TimeSpan.FromSeconds(5) && double.IsFinite(s.CollectorX) && double.IsFinite(s.CollectorY) &&
            Distance(s.CollectorX,s.CollectorY,center.X,center.Y)<=center.Radius).ToArray();
        var observedAt=freshNative.Length>0?freshNative.Max(s=>s.ObservedAt):radar.CapturedAtUtc;
        if (!complete || !_collectionBoundary.CoversCenter(center) || !radar.WorldCharacterDataAvailable || !radar.LivePlayerPositionAvailable ||
            !double.IsFinite(radar.PlayerX) || !double.IsFinite(radar.PlayerY) ||
            !double.IsFinite(center.X) || !double.IsFinite(center.Y) ||
            !radar.IsInsideCenterZone || Distance(radar.PlayerX,radar.PlayerY,center.X,center.Y)>center.Radius ||
            observedAt<started || _clock()-observedAt>TimeSpan.FromSeconds(5) ||
            observedAt>_clock().AddSeconds(5)) return 0;
        var present=radar.Traders.Where(t=>t.IsVisible && t.KioskType is 1 or 3 or 8)
            .Select(t=>CycleQueue.Key(t.Name)).Where(k=>k.Length>0).ToHashSet();
        foreach(var state in freshNative)
            if(state.KioskType is 1 or 3 or 8)
                present.Add(CycleQueue.Key(state.Name));
        // Startup/recovery can expose an empty hook cache before world packets arrive.
        if(present.Count==0)return 0;
        var removed=0;
        lock(_sync)Transaction(()=>{
            foreach(var t in _traders.Values.Where(t=>t.HasPosition && (!t.ConfirmedClosed || _serverActiveRosterKeys.Contains(t.Key)) &&
                _collectionBoundary.Inside(t.X,t.Y) && !present.Contains(t.Key) &&
                t.ObservedAt<started && t.StateAt<started && (t.LastRead is null || t.LastRead<started)))
            {
                Invalidate(t,"Absent from confirmed center radar",observedAt);
                t.ClosedThisSession=true;t.ConfirmedClosed=true;t.StateAt=observedAt;
                t.ObjectId=0;Save(t);_remaining.Remove(t.Key);
                var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(t.Key)))[..24];
                var id=$"center-{epochId}-{hash}";
                AddOutbox(id,"state","state",new {eventId=id,sourceId=$"collector:{_sourceProfile:N}",sessionId=_session,
                    market=_profile.Name,city="Giran",traderKey=t.Key,displayName=t.Name,
                    observedAtUtc=observedAt,type="center_absent",verificationRevision=t.VerificationToken,
                    kioskType=t.KioskType,x=t.X,y=t.Y,currentConfirmation=true,
                    collectorX=radar.PlayerX,collectorY=radar.PlayerY,centerX=center.X,centerY=center.Y,
                    centerScanStartedAtUtc=started,centerScanComplete=true,centerVisibleTraders=present.Count});
                removed++;
                Message?.Invoke($"INFO {_profile.Name}: trader={t.Key} · absent from confirmed center radar · retirement queued");
            }
        });
        WakeSender();return removed;
    }
}
