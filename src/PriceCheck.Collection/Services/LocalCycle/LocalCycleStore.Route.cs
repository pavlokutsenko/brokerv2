using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed partial class LocalCycleStore
{
    private string _pass="";
    private readonly List<string> _remaining=[];
    private readonly HashSet<string> _admitted=[];
    private readonly HashSet<string> _admittedGenerations=[];
    private readonly HashSet<string> _readPass=[];
    private int _admissionLimit;
    private int _initialCount;
    private int _admissionCount;
    public bool HasPendingPass { get { lock(_sync) return _pass.Length>0 && _remaining.Any(k=>_traders.TryGetValue(k,out var t) && Ready(t)); } }
    public void ResetPass()
    {
        lock(_sync){_pass="";_remaining.Clear();_admitted.Clear();_admittedGenerations.Clear();_readPass.Clear();_initialCount=0;_admissionCount=0;_admissionLimit=0;_db.Command("DELETE FROM route");}
    }
    public void BeginPass(double x,double y,CycleRadarPool boundary)
    {
        lock(_sync) Transaction(()=>{
            _pass=Guid.NewGuid().ToString("N"); _remaining.Clear(); _admitted.Clear();_admittedGenerations.Clear(); _readPass.Clear();
            _db.Command("DELETE FROM route");
            var candidates=_traders.Values.Where(t=>Ready(t)&&boundary.Inside(t.X,t.Y)).ToList();
            // Coarse clusters cover the whole market; detailed worker path gets only 20 targets.
            var clusters=candidates.GroupBy(t=>CycleRouteSections.Room(t.X,t.Y) is {Length:>0} room
                ? room : $"grid:{(int)Math.Floor(t.X/256)}:{(int)Math.Floor(t.Y/256)}").Select(g=>g.ToList()).ToList();
            while(clusters.Count>0)
            {
                var cluster=clusters.MinBy(g=>Distance(x,y,g.Average(t=>t.X),g.Average(t=>t.Y)))!;
                clusters.Remove(cluster);
                while(cluster.Count>0)
                {
                    var t=cluster.MinBy(t=>Distance(x,y,t.X,t.Y))!;cluster.Remove(t);
                    _remaining.Add(t.Key);_admitted.Add(t.Key);x=t.X;y=t.Y;
                    _admittedGenerations.Add($"{t.Key}:{t.VerificationToken}");
                    PersistRoute(t,_remaining.Count);
                }
            }
            _initialCount=_remaining.Count;
            _admissionCount=_initialCount;
            // Finite admission allowance: continuously arriving shops cannot postpone center forever.
            _admissionLimit=_initialCount+Math.Max(64,_initialCount/2);
            Message?.Invoke($"INFO {_profile.Name}: route {_pass} · {_initialCount} targets · admission cap {_admissionLimit}");
        });
    }
    public bool ContinuePass(double x,double y,CycleRadarPool boundary)
    {
        lock(_sync)
        {
            if(_pass.Length==0 || !double.IsFinite(x) || !double.IsFinite(y))return false;
            Transaction(()=>{
                var pending=_remaining.Where(k=>_traders.TryGetValue(k,out var t) && Ready(t) && boundary.Inside(t.X,t.Y))
                    .Select(k=>_traders[k]).ToList();
                _remaining.Clear();
                var clusters=pending.GroupBy(t=>CycleRouteSections.Room(t.X,t.Y) is {Length:>0} room
                    ? room : $"grid:{(int)Math.Floor(t.X/256)}:{(int)Math.Floor(t.Y/256)}").Select(g=>g.ToList()).ToList();
                while(clusters.Count>0)
                {
                    var cluster=clusters.MinBy(g=>Distance(x,y,g.Average(t=>t.X),g.Average(t=>t.Y)))!;
                    clusters.Remove(cluster);
                    while(cluster.Count>0)
                    {
                        var t=cluster.MinBy(t=>Distance(x,y,t.X,t.Y))!;cluster.Remove(t);
                        _remaining.Add(t.Key);x=t.X;y=t.Y;
                        _db.Command("UPDATE route SET ordinal=? WHERE pass_id=? AND trader_key=?",_remaining.Count,_pass,t.Key);
                    }
                }
            });
            Message?.Invoke($"INFO {_profile.Name}: continuing route {_pass} · {_remaining.Count} remaining targets from new character position");
            return _remaining.Count>0;
        }
    }
    private bool Ready(LocalTrader t) => t.HasPosition && !t.ClosedThisSession && !t.ConfirmedClosed && !t.NeedsServerHistory && t.KioskType is 1 or 3 or 8 && NeedsRead(t);
    private void InsertPassTarget(LocalTrader t,double x,double y)
    {
        var generation=$"{t.Key}:{t.VerificationToken}";
        if(_pass.Length==0 || !Ready(t) || _remaining.Contains(t.Key) || _admittedGenerations.Contains(generation) || _admissionCount>=_admissionLimit) return;
        _admittedGenerations.Add(generation);_admissionCount++;
        _admitted.Add(t.Key);
        // Insert only into the nearest upcoming segment. A shop behind the current direction goes to the end.
        var index=_remaining.Count;
        if(_remaining.Count>0 && _traders.TryGetValue(_remaining[0],out var forward))
        {
            var dot=(forward.X-x)*(t.X-x)+(forward.Y-y)*(t.Y-y);
            if(dot>=0)
            {
                var nearest=_remaining.Take(20).Select((key,i)=>(key,i)).MinBy(v=>Distance(t.X,t.Y,_traders[v.key].X,_traders[v.key].Y));
                index=nearest.i;
            }
        }
        _remaining.Insert(index,t.Key);PersistRoute(t,_admitted.Count);
        Message?.Invoke($"INFO {_profile.Name}: route insert {t.Name} trader={t.Key} · section {index/20+1}");
    }
    private void PersistRoute(LocalTrader t,int ordinal) => _db.Command("INSERT INTO route(pass_id,ordinal,trader_key,revision) VALUES(?,?,?,?) ON CONFLICT(pass_id,trader_key) DO UPDATE SET ordinal=excluded.ordinal,revision=excluded.revision,done=0,attempts=0",_pass,ordinal,t.Key,t.Revision);
    public IReadOnlyList<CycleTarget> NextTargets(int count=20)
    {
        lock(_sync)
        {
            var targets=new List<CycleTarget>();
            foreach(var key in _remaining.ToArray())
            {
                var t=_traders[key];
                if(!Ready(t)) { _remaining.Remove(key); continue; }
                targets.Add(new(t.Key,t.Name,t.ObjectId,t.KioskType,t.X,t.Y,t.Revision,t.Reason,t.ChangedAt)
                    {RebindOnRead=t.ObjectId==0,VerificationRevision=t.VerificationToken});
                if(targets.Count>=count) break;
            }
            return targets;
        }
    }
    public void FinishAttempt(string key,string? error=null)
    {
        lock(_sync) Transaction(()=>{
            _remaining.Remove(key);
            _db.Command("UPDATE route SET done=1,attempts=attempts+1 WHERE pass_id=? AND trader_key=?",_pass,key);
            if(!_traders.TryGetValue(key,out var t)) return;
            if(error is not null)
            {
                t.Error=error;Save(t);
                var attempts=int.Parse(_db.Query("SELECT attempts FROM route WHERE pass_id=? AND trader_key=?",_pass,key).FirstOrDefault()?[0]??"2");
                if(attempts==1 && Ready(t))
                {
                    _remaining.Add(key);
                    Message?.Invoke($"WARNING {_profile.Name}: {t.Name} trader={t.Key} · {error} · one retry at end of pass");
                }
                else Message?.Invoke($"WARNING {_profile.Name}: {t.Name} trader={t.Key} unresolved · {error}");
            }
        });
    }
    public MarketCycleStatus Status(MarketCycleStatus state)
    {
        lock(_sync)
        {
            var targets=_remaining.Where(k=>Ready(_traders[k])).Select(k=>_traders[k]).ToArray();
            return state with {Active=_traders.Values.Count(t=>!t.ConfirmedClosed),Pending=targets.Length,Checked=_readPass.Count,
                CurrentPriceTraders=_traders.Values.Count(t=>t.SeenOpenThisSession && t.HasPosition && _collectionBoundary.Inside(t.X,t.Y) &&
                    !t.ClosedThisSession && !t.ConfirmedClosed && !t.NeedsServerHistory && t.KioskType is 1 or 3 or 8 && !NeedsRead(t)),
                Deferred=0,Overdue=0,PassRead=_readPass.Count,PassNewFound=Math.Max(0,_admitted.Count-_initialCount),PassRadarPending=targets.Length,
                Queue=targets.Take(150).Select(t=>new CycleQueueRow(t.Name,"Ready",t.Reason,
                    t.LastRead is null?"Never read":$"{(_clock()-t.LastRead.Value).TotalHours:F1} h",0)).ToArray()};
        }
    }
}
