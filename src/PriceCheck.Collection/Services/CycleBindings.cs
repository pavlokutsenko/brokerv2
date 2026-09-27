using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

// Runtime bindings only. No prices, retry deadlines or durable queue on this PC.
public sealed class CycleBindings
{
    private readonly object _sync=new();
    private readonly Dictionary<string,RadarPoint> _known=[];
    public void BeginSession()
    {
        lock(_sync) foreach(var key in _known.Keys.ToArray())
            _known[key]=_known[key] with {ObjectId=0,IsVisible=false};
    }
    public void AcceptExact(CycleTarget target,DateTimeOffset capturedAt)
    {
        if(target.ObjectId<=0 || target.ObjectId>int.MaxValue || target.KioskType is not (1 or 3 or 8) ||
            !double.IsFinite(target.X) || !double.IsFinite(target.Y)) return;
        lock(_sync) _known[target.TraderKey]=new((int)target.ObjectId,target.Name,target.KioskType,target.X,target.Y,0,true,capturedAt);
    }
    public IReadOnlyList<string> Keys { get { lock(_sync) return _known.Keys.ToArray(); } }
    public HashSet<string> Apply(BrokerInventoryFile inventory,RadarSnapshot radar)
    {
        lock(_sync)
        {
        var positions=radar.Traders.GroupBy(t=>CycleQueue.Key(t.Name)).ToDictionary(g=>g.Key,g=>g.Last());
        var reopened=new HashSet<string>();var present=new HashSet<string>();
        foreach(var group in inventory.Rows.Where(r=>!string.IsNullOrWhiteSpace(r.TraderName)).GroupBy(r=>CycleQueue.Key(r.TraderName)))
        {
            present.Add(group.Key);
            if(!positions.TryGetValue(group.Key,out var point)) continue;
            if(_known.TryGetValue(group.Key,out var old) && (old.ObjectId!=point.ObjectId || old.KioskType!=point.KioskType)) reopened.Add(group.Key);
            _known[group.Key]=point;
        }
        if(inventory.Complete) foreach(var key in _known.Keys.Where(k=>!present.Contains(k)).ToArray()) _known.Remove(key);
        return reopened;
        }
    }
    public void Observe(RadarSnapshot radar)
    {
        lock(_sync)
        {
        foreach(var point in radar.Traders.Where(t=>t.IsVisible))
        {
            var key=CycleQueue.Key(point.Name);
            // Admission and generation changes wait for another broker pass.
            if(_known.TryGetValue(key,out var old) && old.KioskType==point.KioskType &&
                (old.ObjectId==point.ObjectId || old.ObjectId==0 &&
                 Math.Sqrt(Math.Pow(old.X-point.X,2)+Math.Pow(old.Y-point.Y,2))<20)) _known[key]=point;
        }
        }
    }
    public CycleTarget? Bind(ServerPriceJob job)
        => Bind(job,out _);
    public CycleTarget? Bind(ServerPriceJob job,out string? unavailableReason)
    {
        lock(_sync)
        {
        unavailableReason=null;
        if(!_known.TryGetValue(job.TraderKey,out var point))
        { unavailableReason="No runtime object after broker"; return null; }
        if(point.KioskType!=job.KioskType)
        { unavailableReason=$"Runtime shop type changed ({job.KioskType} to {point.KioskType})"; return null; }
        var moved=Math.Sqrt(Math.Pow(point.X-job.X,2)+Math.Pow(point.Y-job.Y,2));
        if(moved>=20)
        { unavailableReason=$"Runtime shop moved {moved:F1} units since broker"; return null; }
        return new(job.TraderKey,point.Name,point.ObjectId,point.KioskType,point.X,point.Y,0,job.Reason,job.DueAt)
            {ServerJob=job,RebindOnRead=point.ObjectId==0};
        }
    }
}
