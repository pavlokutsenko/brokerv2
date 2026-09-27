using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Runtime.Radar;

// Owned by one RadarSession. Visibility loss does not erase an identity.
public sealed class RadarIdentityCache
{
    private readonly Dictionary<int,RadarPoint> _known=[];
    public int Count => _known.Count;
    public void Apply(WorldPacket packet, DateTimeOffset? observedAt = null)
    {
        var now=observedAt ?? DateTimeOffset.UtcNow;
        switch(packet)
        {
            case CharacterPacket c when c.ObjectId>0 && !string.IsNullOrWhiteSpace(c.Name) && c.Name.Length<=32:
                _known[c.ObjectId]=new(c.ObjectId,c.Name,c.KioskType,c.X,c.Y,0,true,now);
                break;
            case MovePacket m when _known.TryGetValue(m.ObjectId,out var p):
                _known[m.ObjectId]=p with {X=m.X,Y=m.Y,LastSeenAtUtc=now};
                break;
            case DeletePacket d when _known.TryGetValue(d.ObjectId,out var p):
                _known[d.ObjectId]=p with {IsVisible=false};
                break;
        }
    }
    public IReadOnlyList<RadarPoint> Snapshot() => _known.Values.ToArray();
}
