using System.Text.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private static bool TryAcceptCenterEndpoint(JsonElement result,string? reason,MarketZone center,
        RadarSnapshot? radar,int processId,DateTimeOffset now,out double[] endpoint)
    {
        endpoint=[];
        if(reason is not ("recovery_unreachable" or "stalled" or "time_limit") ||
            radar is null || radar.ProcessId!=processId || !radar.WorldCharacterDataAvailable ||
            !radar.LivePlayerPositionAvailable || !radar.CenterZoneConfigured || !radar.IsInsideCenterZone ||
            (now-radar.CapturedAtUtc).Duration()>TimeSpan.FromSeconds(5) ||
            !InsideSavedCenter(radar.PlayerX,radar.PlayerY,center) ||
            !result.TryGetProperty("position",out var position) || position.ValueKind!=JsonValueKind.Array ||
            position.GetArrayLength()<2 || position[0].ValueKind!=JsonValueKind.Number || position[1].ValueKind!=JsonValueKind.Number ||
            !position[0].TryGetDouble(out var x) || !position[1].TryGetDouble(out var y) ||
            !InsideSavedCenter(x,y,center))return false;
        endpoint=[x,y];return true;
    }
    private static bool InsideSavedCenter(double x,double y,MarketZone center)=>
        double.IsFinite(x)&&double.IsFinite(y)&&Math.Sqrt(Math.Pow(x-center.X,2)+Math.Pow(y-center.Y,2))<=500;
}
