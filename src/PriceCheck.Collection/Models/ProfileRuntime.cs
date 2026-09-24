using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PriceCheck.Collector.Models;

public sealed class ProfileRuntime : INotifyPropertyChanged
{
    private string _status = "Not running";
    private bool _isBusy;
    private bool _isCollectionEnabled;
    private bool _readerAttached;
    private string _launchStatus = "Not running";
    private PriceCheck.Contracts.ClientSession? _session;
    private RadarSnapshot? _radar;
    private BrokerSnapshot? _broker;

    public required CollectorProfile Profile { get; init; }
    public PriceCheck.Contracts.ClientSession? Session
    {
        get => _session;
        set
        {
            if (!Set(ref _session, value)) return;
            OnPropertyChanged(nameof(ProcessId));
            OnPropertyChanged(nameof(ProcessLabel));
        }
    }
    public bool ReaderAttached { get => _readerAttached; set => Set(ref _readerAttached, value); }
    public string LaunchStatus { get => _launchStatus; set => Set(ref _launchStatus, value); }
    public int? ProcessId => Session?.ProcessId;
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
    public bool IsCollectionEnabled { get => _isCollectionEnabled; set { if (Set(ref _isCollectionEnabled, value)) NotifyMetrics(); } }
    public RadarSnapshot? Radar { get => _radar; set { if (Set(ref _radar, value)) NotifyMetrics(); } }
    public BrokerSnapshot? Broker { get => _broker; set { if (Set(ref _broker, value)) NotifyMetrics(); } }

    public string RoleLabel => "COLLECTOR";
    public string ProcessLabel => ProcessId is int pid ? $"PID {pid}" : "No process";
    public string RadarTraderCount => Radar is null ? "—" : $"{Radar.Traders.Count:N0} / {Radar.VisibleTraders:N0}";
    public string CenterZoneLabel => Profile.CenterZonesByCity.TryGetValue(Profile.City, out var center)
        ? $"X {center.X:N0}  ·  Y {center.Y:N0}"
        : "Center not set";
    public string CenterZoneState => Radar?.CenterZoneConfigured != true
        ? "Center not set"
        : !IsCollectionEnabled
            ? "Collection stopped"
            : Radar.IsInsideCenterZone ? "Inside zone · collecting" : "Outside zone · paused";
    public string CollectionToggleLabel => IsCollectionEnabled ? "Stop collection" : "Start collection";
    public string CurrentPositionLabel => Radar is null
        ? "Current position unavailable"
        : $"Now X {Radar.PlayerX:N0}  ·  Y {Radar.PlayerY:N0}";
    public string ActorCount => Radar?.PositionedActors.ToString("N0") ?? "—";
    public string BrokerTraderCount => Broker?.UniqueTraders.ToString("N0") ?? "—";
    public string ListingCount => Broker?.ListingRows.ToString("N0") ?? "—";
    public string SellCount => Radar?.Traders.Count(x => x.KioskType == 1).ToString("N0") ?? "—";
    public string BuyCount => Radar?.Traders.Count(x => x.KioskType == 3).ToString("N0") ?? "—";
    public string PackageCount => Radar?.Traders.Count(x => x.KioskType == 8).ToString("N0") ?? "—";
    public string LastRadarLabel => Radar is null ? "No snapshot" : $"Updated {Radar.CapturedAtUtc.ToLocalTime():HH:mm:ss}";
    public string LastBrokerLabel => Broker is null ? "No pass yet" : $"{Broker.CapturedAtUtc.ToLocalTime():HH:mm:ss} · {Broker.ElapsedSeconds:F1} sec";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshProfile()
    {
        OnPropertyChanged(nameof(RoleLabel));
        OnPropertyChanged(nameof(CenterZoneLabel));
        OnPropertyChanged(nameof(CenterZoneState));
        OnPropertyChanged(nameof(CollectionToggleLabel));
        OnPropertyChanged(nameof(CurrentPositionLabel));
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
        OnPropertyChanged(nameof(CollectionToggleLabel));
        OnPropertyChanged(nameof(CurrentPositionLabel));
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
