using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Collector.Contracts;
using PriceCheck.Collector.Runtime.Radar;

namespace PriceCheck.Collector;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly IProfileStore _profileStore = new ProfileStore();
    private readonly IClientProcessService _processes = new ClientProcessService();
    private readonly RadarSessionManager _radarSessions = new();
    private readonly MarketApiClient _marketApi = new();
    private readonly Dictionary<Guid, string> _lastUploadedRadarFingerprints = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastRadarUploads = [];
    private readonly SemaphoreSlim _brokerCycleGate = new(1, 1);
    private readonly HashSet<Guid> _brokerRunningProfiles = [];
    private readonly Dictionary<Guid, DateTimeOffset> _nextBrokerRuns = [];
    private readonly HashSet<Guid> _priceWorkerProfiles = [];
    private readonly Dictionary<Guid, DateTimeOffset> _nextPriceClaims = [];
    private readonly DispatcherTimer _refreshTimer;
    private ProfileRuntime? _selectedRuntime;
    private bool _loaded;
    private bool _refreshing;

    public ObservableCollection<ProfileRuntime> Runtimes { get; } = [];
    public ObservableCollection<string> Events { get; } = [];
    public IReadOnlyList<CollectorRoleOption> RoleOptions => CollectorRoleOption.All;
    public IReadOnlyList<string> MarketOptions { get; } = ["Gamma", "Black", "White", "Carmine"];
    public IReadOnlyList<string> CityOptions { get; } = ["Giran", "Gludio"];

    public ProfileRuntime? SelectedRuntime
    {
        get => _selectedRuntime;
        set
        {
            if (_selectedRuntime == value) return;
            _selectedRuntime = value;
            OnPropertyChanged();
            _ = RefreshSelectedAsync();
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += async (_, _) => await RefreshSelectedAsync();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => ShutdownOwnedClients();
    }

    private Task SaveProfilesAsync() =>
        _profileStore.SaveAsync(Runtimes.Select(runtime => runtime.Profile));

    private static MarketZone? GetCenterZone(CollectorProfile profile) =>
        profile.CenterZonesByCity.TryGetValue(profile.City, out var center) &&
        double.IsFinite(center.X) && double.IsFinite(center.Y)
            ? new MarketZone(center.X, center.Y, 500)
            : null;

    private void Log(string value)
    {
        Events.Insert(0, $"{DateTime.Now:HH:mm:ss}  {value}");
        while (Events.Count > 100) Events.RemoveAt(Events.Count - 1);
    }

    private void ShutdownOwnedClients()
    {
        _refreshTimer.Stop();
        _marketApi.Dispose();
        _radarSessions.DisposeAsync().AsTask().GetAwaiter().GetResult();
        foreach (var pid in Runtimes.Select(value => value.ProcessId).OfType<int>().Distinct())
            _processes.Terminate(pid);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
