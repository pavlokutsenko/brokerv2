using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PriceCheck.Collector.Models;

public sealed partial class ProfileRuntime : INotifyPropertyChanged
{
    private string _status = "Не запущен";
    private bool _isBusy;
    private bool _isCollectionEnabled;
    private bool _marketCollectionEnabled;
    private int _accountProcessCount;
    private string _accountsStatus="";
    private bool _readerAttached;
    private string _launchStatus = "Не запущен";
    private PriceCheck.Contracts.ClientSession? _session;
    private RadarSnapshot? _radar;
    private RadarSnapshot? _marketRadar;
    private BrokerSnapshot? _broker;
    private PriceCheck.Collector.Services.BrokerDeliveryStatus? _brokerDelivery;
    private MarketCycleStatus _cycle = new();
    private string _uploadStatus = "Локальный outbox · ожидание данных";
    private string _characterRotationStatus = "Ротация персонажей выключена";
    private PriceCheck.Contracts.ClientProtectionStatus _protection = PriceCheck.Contracts.ClientProtectionStatus.Pending;
    public PriceCheck.Contracts.ClientProtectionStatus Protection { get => _protection; set => Set(ref _protection, value); }

    public required CollectorProfile Profile { get; init; }
    public string? ClientFault { get; set; }
    public PriceCheck.Contracts.ClientSession? Session
    {
        get => _session;
        set
        {
            if (!Set(ref _session, value)) return;
            ResetRadarCounts();
            OnPropertyChanged(nameof(ProcessId));
            OnPropertyChanged(nameof(ProcessLabel));
            OnPropertyChanged(nameof(CanEditMarket));
        }
    }
    public bool ReaderAttached { get => _readerAttached; set { if (Set(ref _readerAttached, value)) OnPropertyChanged(nameof(CanEditMarket)); } }
    public string LaunchStatus { get => _launchStatus; set => Set(ref _launchStatus, value); }
    public int? ProcessId => Session?.ProcessId;
    public string Status { get => _status; set => Set(ref _status, value); }
    public string UploadStatus { get => _uploadStatus; set => Set(ref _uploadStatus, value); }
    public string CharacterRotationStatus { get => _characterRotationStatus; set => Set(ref _characterRotationStatus,value); }
    public bool IsBusy { get => _isBusy; set { if (Set(ref _isBusy, value)) OnPropertyChanged(nameof(CanEditMarket)); } }
    public bool IsCollectionEnabled { get => _isCollectionEnabled; set { if (Set(ref _isCollectionEnabled, value)) { NotifyMetrics(); OnPropertyChanged(nameof(CanEditMarket)); } } }
    public bool MarketCollectionEnabled { get => _marketCollectionEnabled; set { if(Set(ref _marketCollectionEnabled,value)) { OnPropertyChanged(nameof(CollectionToggleLabel)); OnPropertyChanged(nameof(NextBrokerLabel)); } } }
    public int AccountProcessCount { get => _accountProcessCount; set { if(Set(ref _accountProcessCount,value)) { OnPropertyChanged(nameof(ProcessLabel));OnPropertyChanged(nameof(CanEditMarket)); } } }
    public string AccountsStatus { get => _accountsStatus; set => Set(ref _accountsStatus,value); }
    public bool OneTraderPerTurn { get; set; }
    public bool CanEditMarket => AccountProcessCount==0 && !IsBusy && Session is null && !ReaderAttached && !IsCollectionEnabled;

    public bool TryChangeMarket(string previousMarket, string requestedMarket)
    {
        // Two-way WPF binding may already have written the selected value.
        if (previousMarket != requestedMarket && !CanEditMarket)
        {
            Profile.Name = previousMarket;
            RefreshProfile();
            return false;
        }
        Profile.Name = requestedMarket;
        RefreshProfile();
        return true;
    }
    public RadarSnapshot? Radar { get => _radar; set { if (Set(ref _radar, value)) { NotifyRadarCounts(); NotifyMetrics(); } } }
    public RadarSnapshot? MarketRadar { get => _marketRadar; set { if (Set(ref _marketRadar,value)) OnPropertyChanged(nameof(MarketRadarCountLabel)); } }
    public string MarketRadarCountLabel => MarketRadar is null ? "—" : $"{MarketRadar.Traders.Count:N0} · снимок {MarketRadar.CapturedAtUtc.ToLocalTime():HH:mm:ss}";
    public string ServerPriceCountLabel => Cycle.ServerCurrentPriceTraders is int count
        ? $"{count:N0} · сервер {Cycle.ServerPriceCountAt?.ToLocalTime():HH:mm:ss}" : "ожидание сервера";
    public BrokerSnapshot? Broker { get => _broker; set { if (Set(ref _broker, value)) NotifyMetrics(); } }
    public PriceCheck.Collector.Services.BrokerDeliveryStatus? BrokerDelivery
    {
        get => _brokerDelivery;
        set { if (Set(ref _brokerDelivery,value)) OnPropertyChanged(nameof(BrokerSentTraderCount)); }
    }
    public string BrokerSentTraderCount => BrokerDelivery is null ? "—" : $"{BrokerDelivery.Accepted:N0} / {BrokerDelivery.Traders:N0}";
    public MarketCycleStatus Cycle { get => _cycle; set { if (Set(ref _cycle, value)) { OnPropertyChanged(nameof(NextBrokerLabel)); OnPropertyChanged(nameof(ServerPriceCountLabel)); } } }
    public string NextBrokerLabel => IsCollectionEnabled || MarketCollectionEnabled ? "Следующий брокер: после прохода цен" : "Сбор остановлен";

    public string RoleLabel => "СБОРЩИК";
    public string ProcessLabel => AccountProcessCount>1 ? $"{AccountProcessCount} клиентов" :
        ProcessId is int pid ? $"PID {pid}" :
        AccountProcessCount==1 ? "1 клиент" : "Нет процесса";
    public string RadarTraderCount => Radar is null ? "—" : $"{Radar.Traders.Count:N0} / {Radar.VisibleTraders:N0}";
    public string CenterZoneLabel => SavedGiranCenter.Resolve(Profile) is { } center
        ? $"X {center.X:N0}  ·  Y {center.Y:N0}"
        : "Центр не задан";
    public string CenterZoneState => SavedGiranCenter.Resolve(Profile) is null
        ? "Центр не задан"
        : Radar is null ? "Позиция появится после запуска клиента"
        : !Radar.CenterZoneConfigured ? "Позиция недоступна"
        : !IsCollectionEnabled && !MarketCollectionEnabled
            ? "Сбор остановлен"
            : Radar.IsInsideCenterZone ? "В зоне наблюдения" : "Маршрут цен · вне центра";
    public string CollectionToggleLabel => IsCollectionEnabled || MarketCollectionEnabled ? "Остановить сбор" : "Начать сбор";
    public string CurrentPositionLabel => Radar is null
        ? "Текущая позиция недоступна"
        : $"Сейчас X {Radar.PlayerX:N0}  ·  Y {Radar.PlayerY:N0}";
    public string ActorCount => Radar?.PositionedActors.ToString("N0") ?? "—";
    public string BrokerTraderCount => Broker?.UniqueTraders.ToString("N0") ?? "—";
    public string ListingCount => Broker?.ListingRows.ToString("N0") ?? "—";
    public string SellCount => Radar?.Traders.Count(x => x.KioskType == 1).ToString("N0") ?? "—";
    public string BuyCount => Radar?.Traders.Count(x => x.KioskType == 3).ToString("N0") ?? "—";
    public string PackageCount => Radar?.Traders.Count(x => x.KioskType == 8).ToString("N0") ?? "—";
    public string LastRadarLabel => Radar is null ? "Нет снимка" : $"Обновлено {Radar.CapturedAtUtc.ToLocalTime():HH:mm:ss}";
    public string LastBrokerLabel => Broker is null ? "Проходов пока нет" : $"{Broker.CapturedAtUtc.ToLocalTime():HH:mm:ss} · {Broker.ElapsedSeconds:F1} с";

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
