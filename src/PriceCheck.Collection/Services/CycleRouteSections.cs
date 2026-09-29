using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

// Room boundaries are surveyed map data, never a second collection boundary.
public static class CycleRouteSections
{
    public static string Room(double x,double y)
        => x>=83881 && x<=84541 && y>=147980 && y<=149260 ? "GiranTemple" : "";

    public static IReadOnlyList<CycleTarget> Select(IReadOnlyList<CycleTarget> ready,
        IReadOnlySet<string>? excluded=null)
    {
        var candidates=ready.Where(t=>excluded is null || !excluded.Contains(t.TraderKey)).ToArray();
        if(candidates.Length==0)return [];
        var room=Room(candidates[0].X,candidates[0].Y);
        if(room.Length>0)return candidates.Where(t=>Room(t.X,t.Y)==room).ToArray();
        return candidates.TakeWhile(t=>Room(t.X,t.Y).Length==0).Take(20).ToArray();
    }
}
