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
    private readonly LaunchTemplateStore _templateStore = new();
    private readonly PriceCheck.Launching.LaunchModule _launcher = new();
    private readonly PriceCheck.Collection.CollectionModule _collection = new();
    public ObservableCollection<PriceCheck.Contracts.ClientSession> AvailableClients { get; } = [];
    public PriceCheck.Contracts.ClientSession? SelectedClient { get; set; }
    private bool _closeReady;
    private bool _closing;
    private readonly DispatcherTimer _refreshTimer;
    private ProfileRuntime? _selectedRuntime;
    private bool _loaded;
    private bool _refreshing;
    private bool _syncingLoginPassword;

    public string? StartupLaunchProfileName { get; init; }

    public ObservableCollection<ProfileRuntime> Runtimes { get; } = [];
    public ObservableCollection<LaunchTemplate> LaunchTemplates { get; } =
        [new LaunchTemplate { Id = Guid.Empty, Name = "No template", HardwareEnabled = false }];
    public string SelectedTemplateSummary =>
        LaunchTemplates.FirstOrDefault(value => value.Id == SelectedRuntime?.Profile.LaunchTemplateId)?.Summary ??
        "No HWID override or proxy";
    public ObservableCollection<string> Events { get; } = [];
    public IReadOnlyList<CollectorRoleOption> RoleOptions => CollectorRoleOption.All;
    public IReadOnlyList<string> MarketOptions { get; } = ["Gamma", "Black", "White", "Carmine"];
    public IReadOnlyList<string> LoginServerOptions { get; } = ["Gamma"];
    public IReadOnlyList<CharacterSlotOption> CharacterOptions { get; } =
        Enumerable.Range(0, 7).Select(slot => new CharacterSlotOption(slot, $"Slot {slot + 1}")).ToArray();
    public IReadOnlyList<string> CityOptions { get; } = ["Giran", "Gludio"];

    public ProfileRuntime? SelectedRuntime
    {
        get => _selectedRuntime;
        set
        {
            if (_selectedRuntime == value) return;
            _selectedRuntime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedTemplateSummary));
            SyncLoginPasswordField();
            _ = RefreshSelectedAsync();
        }
    }

    public MainWindow() : this(true) { }

    // Allows rendering the actual window with synthetic data and no application lifecycle.
    public MainWindow(bool initializeRuntime)
    {
        InitializeComponent();
        DataContext = this;
        TemplatesView.SaveRequested = ApplyTemplatesAsync;
        _collection.Message += Log;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += async (_, _) => await RefreshAllAsync();
        if (initializeRuntime)
        {
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }
    }

    private Task SaveProfilesAsync() =>
        _profileStore.SaveAsync(Runtimes.Select(runtime => runtime.Profile));

    private void SyncLoginPasswordField()
    {
        if (LaunchPanel.LoginPasswordBox is null) return;
        _syncingLoginPassword = true;
        try { LaunchPanel.LoginPasswordBox.Password = SelectedRuntime?.Profile.LoginPassword ?? ""; }
        finally { _syncingLoginPassword = false; }
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record CharacterSlotOption(int Slot, string Label);
