using System.Text;

namespace PriceCheck.Collector.Models;

public sealed partial class ProfileRuntime
{
    private int? _centerTraderCount;
    private DateTimeOffset? _centerCountAt;
    public string CurrentRadarTraderCount => Radar is { LivePlayerPositionAvailable: true, WorldCharacterDataAvailable: true } radar
        ? VisibleTraderCount(radar).ToString("N0") : "—";
    public string CenterRadarTraderCount => _centerTraderCount?.ToString("N0") ?? "—";
    public string CenterRadarCountAge => _centerCountAt is { } at ? $"снимок {at.ToLocalTime():HH:mm:ss}" : "ожидание центра";

    private static int VisibleTraderCount(RadarSnapshot radar) => radar.Traders
        .Where(t => t.IsVisible && t.KioskType is 1 or 3 or 8)
        .Select(t => t.Name.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant())
        .Where(key => key.Length > 0).Distinct().Count();
    public void ConfirmCenterRadarTraderCount(int traderCount, DateTimeOffset capturedAtUtc)
    {
        _centerTraderCount = traderCount;
        _centerCountAt = capturedAtUtc;
        NotifyRadarCounts();
    }
    private void ResetRadarCounts()
    {
        _centerTraderCount = null; _centerCountAt = null;
        NotifyRadarCounts();
    }
    private void NotifyRadarCounts()
    {
        OnPropertyChanged(nameof(CurrentRadarTraderCount));
        OnPropertyChanged(nameof(CenterRadarTraderCount));
        OnPropertyChanged(nameof(CenterRadarCountAge));
    }
}
