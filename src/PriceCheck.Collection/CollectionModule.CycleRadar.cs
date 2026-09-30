using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private static void WriteCycleRadarTargets(CycleRun cycle,RadarSnapshot radar)
    {
        var path=cycle.RadarFile;
        if(path is null || DateTimeOffset.UtcNow<cycle.NextRadarWrite) return;
        cycle.NextRadarWrite=DateTimeOffset.UtcNow.AddMilliseconds(500);
        var targets=cycle.Store.NextTargets(100)
            .Where(t=>cycle.ActiveTraderKey is null || t.TraderKey==cycle.ActiveTraderKey)
            .Select(t=>new {name=t.Name,x=t.X,y=t.Y,object_id=t.ObjectId,kiosk_type=t.KioskType,observed_at=t.DueAt,
                reopened=t.Reason=="Shop reopened",verification_revision=t.VerificationRevision??t.Revision.ToString()})
            .ToArray();
        try
        {
            var temp=path+".tmp";
            var closed=radar.ClosedTraders.Select(t=>new {name=t.Name,object_id=t.ObjectId,
                kiosk_type=t.KioskType,observed_at=t.LastSeenAtUtc}).ToArray();
            var bindings=radar.Traders.Where(t=>t.IsVisible)
                .Select(t=>new {name=t.Name,x=t.X,y=t.Y,object_id=t.ObjectId,kiosk_type=t.KioskType}).ToArray();
            File.WriteAllText(temp,JsonSerializer.Serialize(new {at=DateTimeOffset.UtcNow,pid=radar.ProcessId,traders=targets,closed,bindings}));
            File.Move(temp,path,true);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { } // Retry a temporarily locked frame.
    }

    private static void UpdateCycleRadarCounters(ProfileRuntime runtime,CycleRun cycle)
    {
        runtime.Cycle=cycle.Store.Status(runtime.Cycle);
    }
}
