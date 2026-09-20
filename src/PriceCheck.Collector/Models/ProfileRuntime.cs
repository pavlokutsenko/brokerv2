using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PriceCheck.Collector.Models;

public sealed class ProfileRuntime : INotifyPropertyChanged
{
    private int? _processId;
    private string _status = "Не запущен";
    private bool _isBusy;
    private bool _isCollectionEnabled;
    private RadarSnapshot? _radar;
    private BrokerSnapshot? _broker;

    public required CollectorProfile Profile { get; init; }
    public int? ProcessId { get => _processId; set => Set(ref _processId, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
    public bool IsCollectionEnabled { get => _isCollectionEnabled; set { if (Set(ref _isCollectionEnabled, value)) NotifyMetrics(); } }
    public RadarSnapshot? Radar { get => _radar; set { if (Set(ref _radar, value)) NotifyMetrics(); } }
    public BrokerSnapshot? Broker { get => _broker; set { if (Set(ref _broker, value)) NotifyMetrics(); } }

    public string RoleLabel => Profile.Role == CollectorRole.BrokerRadar ? "БРОКЕР" : "ЦЕНЫ";
    public string ProcessLabel => ProcessId is int pid ? $"PID {pid}" : "нет процесса";
    public string RadarTraderCount => Radar is null ? "—" : $"{Radar.Traders.Count:N0} / {Radar.VisibleTraders:N0}";
    public string CenterZoneLabel => Profile.CenterZoneX is double x && Profile.CenterZoneY is double y
        ? $"X {x:N0}  ·  Y {y:N0}"
        : "Центр не отмечен";
    public string CenterZoneState => Radar?.CenterZoneConfigured != true
        ? "Центр не отмечен"
        : !IsCollectionEnabled
            ? "Сбор остановлен"
            : Radar.IsInsideCenterZone ? "В зоне · сбор активен" : "Вне зоны · сбор на паузе";
    public string ActorCount => Radar?.PositionedActors.ToString("N0") ?? "—";
    public string BrokerTraderCount => Broker?.UniqueTraders.ToString("N0") ?? "—";
    public string ListingCount => Broker?.ListingRows.ToString("N0") ?? "—";
    public string SellCount => Radar?.Traders.Count(x => x.KioskType == 1).ToString("N0") ?? "—";
    public string BuyCount => Radar?.Traders.Count(x => x.KioskType == 3).ToString("N0") ?? "—";
    public string PackageCount => Radar?.Traders.Count(x => x.KioskType == 8).ToString("N0") ?? "—";
    public string LastRadarLabel => Radar is null ? "снимка нет" : $"обновлено {Radar.CapturedAtUtc.ToLocalTime():HH:mm:ss}";
    public string LastBrokerLabel => Broker is null ? "прохода нет" : $"{Broker.CapturedAtUtc.ToLocalTime():HH:mm:ss} · {Broker.ElapsedSeconds:F1} сек";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshProfile()
    {
        OnPropertyChanged(nameof(RoleLabel));
        OnPropertyChanged(nameof(CenterZoneLabel));
        OnPropertyChanged(nameof(CenterZoneState));
        OnPropertyChanged(nameof(Profile));
    }

    private void NotifyMetrics()
    {
        OnPropertyChanged(nameof(RadarTraderCount));
        OnPropertyChanged(nameof(ActorCount));
        OnPropertyChanged(nameof(BrokerTraderCount));
        OnPropertyChanged(nameof(ListingCount));
        OnPropertyChanged(nameof(SellCount));
        OnPropertyChanged(nameof(BuyCount));
        OnPropertyChanged(nameof(PackageCount));
        OnPropertyChanged(nameof(LastRadarLabel));
        OnPropertyChanged(nameof(CenterZoneState));
        OnPropertyChanged(nameof(LastBrokerLabel));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(ProcessId)) OnPropertyChanged(nameof(ProcessLabel));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
