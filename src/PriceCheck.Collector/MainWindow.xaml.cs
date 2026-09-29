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
    private readonly IProfileStore _profileStore;
    private readonly LaunchTemplateStore _templateStore;
    private readonly PriceCheck.Launching.LaunchModule _launcher = new();
    private readonly PriceCheck.Launching.CharacterRotationService _characterRotation;
    private readonly PriceCheck.Launching.ClientRecoveryService _clientRecovery;
    private readonly PriceCheck.Collection.CollectionModule _collection = new();
    private bool _closeReady;
    private bool _closing;
    private readonly DispatcherTimer _refreshTimer;
    private ProfileRuntime? _selectedRuntime;
    private bool _loaded;
    private bool _refreshing;
    private bool _syncingLoginPassword;
    private bool _syncingProfileFields;

    public string? StartupLaunchProfileName { get; init; }
    public string? StartupCollectProfileId { get; init; }
    public bool StartupResumeCharacter { get; init; }

    public ObservableCollection<ProfileRuntime> Runtimes { get; } = [];
    public ObservableCollection<LaunchTemplate> LaunchTemplates { get; } =
        [new LaunchTemplate { Id = Guid.Empty, Name = "Без шаблона", HardwareEnabled = false }];
    public string SelectedTemplateSummary =>
        LaunchTemplates.FirstOrDefault(value => value.Id == SelectedRuntime?.Profile.LaunchTemplateId)?.Summary ??
        "Запуск недоступен: выберите шаблон HWID";
    public ObservableCollection<string> Events { get; } = [];
    public IReadOnlyList<CollectorRoleOption> RoleOptions => CollectorRoleOption.All;
    public ObservableCollection<string> MarketOptions { get; } = [];
    public IReadOnlyList<CharacterSlotOption> CharacterOptions =>
        SelectedRuntime?.Profile.RotationCharacterSlots is { Length: > 0 } slots
            ? slots.Select(slot => new CharacterSlotOption(slot, $"Слот {slot}")).ToArray()
            : [new CharacterSlotOption(0, "Слот 0 · после входа")];

    public ProfileRuntime? SelectedRuntime
    {
        get => _selectedRuntime;
        set
        {
            if (_selectedRuntime == value) return;
            _selectedRuntime = value;
            // Rebinding editors raises the same events as user edits. Do not
            // validate the previous editor value against the newly selected profile.
            var wasSyncing = _syncingProfileFields;
            _syncingProfileFields = true;
            try
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedTemplateSummary));
                OnPropertyChanged(nameof(CharacterOptions));
                SyncLoginPasswordField();
            }
            finally { _syncingProfileFields = wasSyncing; }
            _ = RefreshSelectedAsync();
        }
    }

    public MainWindow() : this(true) { }

    // Allows rendering the actual window with synthetic data and no application lifecycle.
    public MainWindow(bool initializeRuntime, string? settingsDirectory = null)
    {
        _profileStore = new ProfileStore(settingsDirectory);
        _templateStore = new LaunchTemplateStore(settingsDirectory);
        _characterRotation=new(_launcher);
        _clientRecovery=new(_launcher);
        InitializeComponent();
        DataContext = this;
        Runtimes.CollectionChanged += (_, _) =>
        {
            foreach (var name in Runtimes.Select(value => value.Profile.Name)
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
                if (!MarketOptions.Contains(name)) MarketOptions.Add(name);
        };
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
