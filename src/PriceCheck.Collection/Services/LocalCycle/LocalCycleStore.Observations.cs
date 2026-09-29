using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed partial class LocalCycleStore
{
    // Runs regardless of active native operation. No HTTP or native work under the DB lock.
    public void Observe(RadarSnapshot radar, CycleRadarPool boundary, MarketZone center, DateTimeOffset centerEntered,
        bool publishNewPresence=false)
    {
        lock (_sync) Transaction(() => {
            _collectionBoundary=boundary;
            foreach (var p in radar.Traders)
            {
                var key = CycleQueue.Key(p.Name);
                if (key.Length == 0 || p.KioskType is not (1 or 3 or 8) || !double.IsFinite(p.X) || !double.IsFinite(p.Y) ||
                    !boundary.Inside(p.X,p.Y) || !_traders.ContainsKey(key) && Distance(p.X,p.Y,radar.PlayerX,radar.PlayerY)>3000) continue;
                if (!_traders.TryGetValue(key,out var t))
                {
                    t = new() {Key=key,Name=p.Name,ChangedAt=p.LastSeenAtUtc}; _traders.Add(key,t);
                    Message?.Invoke($"INFO {_profile.Name}: discovered {p.Name} trader={key}");
                }
                var stateAt = p.StateObservedAtUtc == default ? p.LastSeenAtUtc : p.StateObservedAtUtc;
                if(t.ObjectId==p.ObjectId && t.X==p.X && t.Y==p.Y && t.KioskType==p.KioskType &&
                    t.StateAt==stateAt && t.ObservedAt==p.LastSeenAtUtc && !t.ClosedThisSession)continue;
                if (stateAt < t.StateAt && t.ClosedThisSession) continue;
                var reopen = p.LastReopenedAtUtc is {} reopenAt && reopenAt>t.StateAt ||
                    (t.ClosedThisSession || t.ConfirmedClosed) && stateAt>t.StateAt ||
                    publishNewPresence && !_serverActiveTradingKeys.Contains(key);
                var moved = t.HasPosition && (Distance(t.X,t.Y,p.X,p.Y)>=20 || t.LastRead is not null &&
                    !t.Dirty && Distance(t.CheckedX,t.CheckedY,p.X,p.Y)>=20);
                var changedType = t.KioskType is 1 or 3 or 8 && t.KioskType!=p.KioskType;
                if (reopen || moved || changedType)
                {
                    var at = reopen && p.LastReopenedAtUtc is {} reopened ? reopened : stateAt;
                    var reason = reopen ? "Shop reopened" : moved ? "Shop moved" : "Shop type changed";
                    Invalidate(t,reason,at); StateEvent(t,reopen?"reopened":moved?"moved":"type_changed",at,p,center);
                    Message?.Invoke($"INFO {_profile.Name}: {p.Name} trader={key} · {reason} · revision {t.Revision}");
                }
                t.ClosedThisSession=false; t.ConfirmedClosed=false; t.SeenOpenThisSession=true;
                t.StateAt=stateAt>t.StateAt?stateAt:t.StateAt; t.Name=p.Name; t.KioskType=p.KioskType;
                t.X=p.X; t.Y=p.Y; t.HasPosition=true; t.ObservedAt=p.LastSeenAtUtc;
                t.ObjectId=p.ObjectId; t.Session=_session; Save(t);
                if(publishNewPresence)_serverActiveTradingKeys.Add(key);
                InsertPassTarget(t,radar.PlayerX,radar.PlayerY);
            }
            foreach (var p in radar.ClosedTraders) ObserveClosedCore(p,center,centerEntered,radar.LivePlayerPositionAvailable);
        });
        WakeSender();
    }
    public void ObserveClosed(RadarPoint p, MarketZone center, DateTimeOffset centerEntered, bool livePosition)
    { lock(_sync) Transaction(()=>ObserveClosedCore(p,center,centerEntered,livePosition)); WakeSender(); }
    private void ObserveClosedCore(RadarPoint p, MarketZone center, DateTimeOffset centerEntered, bool livePosition)
    {
        var key=CycleQueue.Key(p.Name);
        var at=p.StateObservedAtUtc==default?p.LastSeenAtUtc:p.StateObservedAtUtc;
        if(p.KioskType!=0 || !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !_collectionBoundary.Inside(p.X,p.Y) ||
            !_traders.TryGetValue(key,out var t) || at<t.StateAt) return;
        var freshCenter = livePosition && p.StateObservedPlayerX is {} x && p.StateObservedPlayerY is {} y &&
            Distance(x,y,center.X,center.Y)<=center.Radius && at>=centerEntered && _clock()-at<=TimeSpan.FromSeconds(5);
        if(!t.ClosedThisSession || at>t.StateAt)
        {
            var transition=!t.ClosedThisSession;
            if(transition) Invalidate(t,"Closed: state confirmation needed",at);
            t.ClosedThisSession=true; t.StateAt=at; t.ObjectId=p.ObjectId;
            if(transition)
            {
                StateEvent(t,"closed_pending",at,p,center);
                Message?.Invoke($"INFO {_profile.Name}: {p.Name} trader={key} closed · approach cancelled · central confirmation pending");
            }
        }
        if(freshCenter && !t.ConfirmedClosed)
        {
            t.ConfirmedClosed=true; StateEvent(t,"closed_confirmed",at,p,center);
            Message?.Invoke($"INFO {_profile.Name}: {p.Name} trader={key} · current closure confirmed inside saved center{center.Radius:F0}");
        }
        Save(t);
    }
    private void StateEvent(LocalTrader t,string type,DateTimeOffset at,RadarPoint? point,MarketZone center)
    {
        var id=$"state-{Guid.NewGuid():N}";
        AddOutbox(id,"state","state",new {eventId=id,sourceId=$"collector:{_sourceProfile:N}",sessionId=_session,
            market=_profile.Name,city="Giran",traderKey=t.Key,displayName=t.Name,observedAtUtc=at,type,verificationRevision=t.VerificationToken,
            kioskType=point?.KioskType??t.KioskType,x=point?.X??t.X,y=point?.Y??t.Y,
            currentConfirmation=type=="closed_confirmed",collectorX=point?.StateObservedPlayerX,
            collectorY=point?.StateObservedPlayerY,centerX=center.X,centerY=center.Y});
    }
}
