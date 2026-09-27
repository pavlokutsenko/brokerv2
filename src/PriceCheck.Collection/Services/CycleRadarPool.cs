using System.Text.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

// Observations belong to one client/pass. Native validation remains worker-owned.
public sealed class CycleRadarPool
{
    private readonly object _sync=new();
    private readonly Dictionary<string,RadarPoint> _pending=[];
    private readonly HashSet<string> _reviewed=[];
    private readonly HashSet<string> _found=[];
    private readonly HashSet<string> _captured=[];
    private readonly Dictionary<string,RadarPoint> _closed=[];
    private readonly HashSet<string> _reopened=[];
    private readonly double[][]? _polygon;
    public CycleRadarPool(double[][]? polygon=null) { _polygon=polygon; }

    public static CycleRadarPool ForCity(string city)
    {
        var root=Path.Combine(AppContext.BaseDirectory,"BrokerRuntime","_internal","navigation","maps");
        var index=Path.Combine(root,"cities.json");
        if(!File.Exists(index)) return new(); // Synthetic module tests have no worker package.
        using var catalog=JsonDocument.Parse(File.ReadAllText(index));
        if(!catalog.RootElement.TryGetProperty(city,out var entry)) return new([]);
        using var map=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,entry.GetString()!)));
        return new(map.RootElement.TryGetProperty("collectionZone",out var zone)
            ? zone.GetProperty("polygon").EnumerateArray().Select(p=>p.EnumerateArray().Select(v=>v.GetDouble()).ToArray()).ToArray()
            : null);
    }

    public void Reset()
    {
        lock(_sync) { _pending.Clear();_reviewed.Clear();_found.Clear();_captured.Clear();_closed.Clear();_reopened.Clear(); }
    }
    public void BeginSession()
    {
        lock(_sync)
        {
            foreach(var key in _pending.Keys.ToArray()) _pending[key]=_pending[key] with {ObjectId=0,IsVisible=false};
            foreach(var key in _closed.Keys.ToArray()) _closed[key]=_closed[key] with {ObjectId=0,IsVisible=false};
        }
    }

    public void Observe(RadarSnapshot radar,IReadOnlySet<string> brokerKeys,bool brokerRunning)
    {
        lock(_sync)
        {
            if(!brokerRunning) foreach(var key in brokerKeys) if(!_reopened.Contains(key)) _pending.Remove(key);
            foreach(var closed in radar.ClosedTraders)
            {
                var key=CycleQueue.Key(closed.Name);
                if(!closed.IsVisible || closed.KioskType!=0) continue;
                if(_closed.TryGetValue(key,out var newer) && newer.LastSeenAtUtc>closed.LastSeenAtUtc) continue;
                if(_pending.TryGetValue(key,out var freshPending) &&
                    (freshPending.ObjectId!=closed.ObjectId || freshPending.LastSeenAtUtc>closed.LastSeenAtUtc)) continue;
                if(_pending.TryGetValue(key,out var pending) && pending.ObjectId==closed.ObjectId &&
                    closed.LastSeenAtUtc>=pending.LastSeenAtUtc) _pending.Remove(key);
                _closed[key]=closed;
            }
            foreach(var t in radar.Traders)
            {
                var key=CycleQueue.Key(t.Name);
                if(_closed.TryGetValue(key,out var closed))
                {
                    if(t.LastSeenAtUtc<=closed.LastSeenAtUtc) continue;
                    _closed.Remove(key);_reviewed.Remove(key);_reopened.Add(key);
                }
                if(!t.IsVisible || t.ObjectId<=0 || t.KioskType is not (1 or 3 or 8) || key.Length==0 ||
                    !double.IsFinite(t.X) || !double.IsFinite(t.Y) || !Inside(t.X,t.Y) ||
                    Math.Sqrt(Math.Pow(t.X-radar.PlayerX,2)+Math.Pow(t.Y-radar.PlayerY,2))>3000 ||
                    _reviewed.Contains(key) || (!brokerRunning && brokerKeys.Contains(key) && !_reopened.Contains(key))) continue;
                if(_pending.Count>=10000 && !_pending.ContainsKey(key)) continue;
                _pending[key]=t;
                if(!brokerKeys.Contains(key)) _found.Add(key);
            }
        }
    }

    public void BrokerCompleted(IReadOnlySet<string> keys)
    {
        lock(_sync)
        {
            foreach(var key in keys) _pending.Remove(key);
            _found.Clear();_found.UnionWith(_pending.Keys);
        }
    }

    public RadarPoint[] Targets() { lock(_sync) return _pending.Values.ToArray(); }
    public RadarPoint[] NewTargets(IReadOnlySet<string> brokerKeys)
    {
        lock(_sync) return _pending.Where(t=>!brokerKeys.Contains(t.Key) || _reopened.Contains(t.Key)).Select(t=>t.Value).ToArray();
    }
    public bool IsReopened(string key) { lock(_sync) return _reopened.Contains(key); }
    public void NativeClosed(string key,int objectId,DateTimeOffset observedAt)
    {
        lock(_sync)
        {
            if(objectId<=0 || !_pending.TryGetValue(key,out var pending) ||
                (pending.ObjectId!=0 && pending.ObjectId!=objectId) ||
                pending.LastSeenAtUtc>observedAt) return;
            _pending.Remove(key);
            // The current worker has validated the fresh identity before emitting closure.
            _closed[key]=pending with {ObjectId=objectId,KioskType=0,LastSeenAtUtc=observedAt};
        }
    }
    public void Review(string key)
    {
        lock(_sync) { _pending.Remove(key);_reviewed.Add(key); }
    }
    public void Capture(string key)
    {
        lock(_sync) { _captured.Add(key);_pending.Remove(key);_reviewed.Add(key); }
    }
    public (int Found,int Pending,int Captured) Counts()
    {
        lock(_sync) return (_found.Count,_pending.Count,_captured.Count);
    }
    public bool Inside(double x,double y)
    {
        if(_polygon is null) return true;
        var inside=false;
        for(var i=0;i<_polygon.Length;i++)
        {
            var a=_polygon[i];var b=_polygon[(i+1)%_polygon.Length];
            var cross=(x-a[0])*(b[1]-a[1])-(y-a[1])*(b[0]-a[0]);
            if(Math.Abs(cross)<1e-7 && x>=Math.Min(a[0],b[0]) && x<=Math.Max(a[0],b[0]) &&
                y>=Math.Min(a[1],b[1]) && y<=Math.Max(a[1],b[1])) return true;
            if((a[1]>y)!=(b[1]>y) && x<(b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]) inside=!inside;
        }
        return inside;
    }
}
