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
    private readonly PriceCheck.Launching.CharacterRotationService _characterRotation;
    private readonly PriceCheck.Launching.ClientRecoveryService _clientRecovery;
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
    public string? StartupCollectProfileId { get; init; }
    public bool StartupResumeCharacter { get; init; }

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
    public IReadOnlyList<CharacterSlotOption> CharacterOptions =>
        SelectedRuntime?.Profile.RotationCharacterSlots is { Length: > 0 } slots
            ? slots.Select(slot => new CharacterSlotOption(slot, $"Slot {slot}")).ToArray()
            : [new CharacterSlotOption(0, "Slot 0 · список после входа")];

    public ProfileRuntime? SelectedRuntime
    {
        get => _selectedRuntime;
        set
        {
            if (_selectedRuntime == value) return;
            _selectedRuntime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedTemplateSummary));
            OnPropertyChanged(nameof(CharacterOptions));
            SyncLoginPasswordField();
            _ = RefreshSelectedAsync();
        }
    }

    public MainWindow() : this(true) { }

    // Allows rendering the actual window with synthetic data and no application lifecycle.
    public MainWindow(bool initializeRuntime)
    {
        _characterRotation=new(_launcher);
        _clientRecovery=new(_launcher);
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
        _loaded ? _profileStore.SaveAsync(Runtimes.Select(runtime => runtime.Profile)) : Task.CompletedTask;

    private void SyncLoginPasswordField()
    {
        if (LaunchPanel.LoginPasswordBox is null) return;
        _syncingLoginPassword = true;
        try { LaunchPanel.LoginPasswordBox.Password = SelectedRuntime?.Profile.LoginPassword ?? ""; }
        finally { _syncingLoginPassword = false; }
    }

    private static MarketZone? GetCenterZone(CollectorProfile profile) =>
        PriceCheck.Collection.CollectionModule.GetCenterZone(profile);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record CharacterSlotOption(int Slot, string Label);
