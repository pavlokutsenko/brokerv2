using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Collector.Contracts;

namespace PriceCheck.Collector;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly IProfileStore _profileStore = new ProfileStore();
    private readonly IClientProcessService _processes = new ClientProcessService();
    private readonly ICollectorDataSource _prototypeData = new PrototypeDataService();
    private readonly DispatcherTimer _refreshTimer;
    private ProfileRuntime? _selectedRuntime;
    private bool _loaded;
    private bool _refreshing;

    public ObservableCollection<ProfileRuntime> Runtimes { get; } = [];
    public ObservableCollection<string> Events { get; } = [];
    public IReadOnlyList<CollectorRoleOption> RoleOptions => CollectorRoleOption.All;

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
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private Task SaveProfilesAsync() =>
        _profileStore.SaveAsync(Runtimes.Select(runtime => runtime.Profile));

    private void Log(string value)
    {
        Events.Insert(0, $"{DateTime.Now:HH:mm:ss}  {value}");
        while (Events.Count > 100) Events.RemoveAt(Events.Count - 1);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
