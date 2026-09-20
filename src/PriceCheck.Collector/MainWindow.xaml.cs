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

    private void Log(string value)
    {
        Events.Insert(0, $"{DateTime.Now:HH:mm:ss}  {value}");
        while (Events.Count > 100) Events.RemoveAt(Events.Count - 1);
    }

    private void ShutdownOwnedClients()
    {
        _refreshTimer.Stop();
        _radarSessions.DisposeAsync().AsTask().GetAwaiter().GetResult();
        foreach (var pid in Runtimes.Select(value => value.ProcessId).OfType<int>().Distinct())
            _processes.Terminate(pid);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
