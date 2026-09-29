using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Launching;

namespace PriceCheck.Launcher;

public sealed record CharacterSlotOption(int Slot, string Label);
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ProfileStore _profiles;
    private readonly LaunchTemplateStore _templates;
    private readonly LaunchModule _launcher = new();
    private readonly CharacterRotationService _rotation;
    private readonly ClientRecoveryService _recovery;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private LaunchRuntime? _selected;
    private bool _loaded, _syncingPassword, _syncingProfileFields, _refreshing, _closing, _closeReady;
    public string? StartupProfiles { get; init; }
    public ObservableCollection<LaunchRuntime> Runtimes { get; } = [];
    public ObservableCollection<LaunchTemplate> LaunchTemplates { get; } = [new() { Id = Guid.Empty, Name = "Без шаблона", HardwareEnabled = false }];
    public ObservableCollection<LauncherJournalEntry> Events { get; } = [];
    public ICollectionView ProfileEvents { get; }
    public IReadOnlyList<CharacterSlotOption> CharacterOptions =>
        SelectedRuntime?.Profile.RotationCharacterSlots is { Length: > 0 } slots
            ? slots.Select(slot => new CharacterSlotOption(slot, $"Слот {slot}")).ToArray()
            : [new(0, "Слот 0 · после входа")];
    public string SelectedTemplateSummary => LaunchTemplates.FirstOrDefault(value => value.Id == SelectedRuntime?.Profile.LaunchTemplateId)?.Summary ?? "Запуск недоступен: выберите шаблон HWID";
    public LaunchRuntime? SelectedRuntime
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            var wasSyncing = _syncingProfileFields;
            _syncingProfileFields = true;
            try { _selected = value; Changed(); Changed(nameof(CharacterOptions));
                Changed(nameof(SelectedTemplateSummary)); SyncPassword(); }
            finally { _syncingProfileFields = wasSyncing; }
            ProfileEvents?.Refresh();
        }
    }
    public MainWindow() : this(true) { }
    public MainWindow(bool initializeRuntime, string? settingsDirectory = null)
    {
        var root = settingsDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceCheckLauncher");
        _profiles = new(root); _templates = new(root);
        _rotation = new(_launcher); _recovery = new(_launcher);
        ProfileEvents = CollectionViewSource.GetDefaultView(Events);
        ProfileEvents.Filter = value => value is LauncherJournalEntry entry &&
            entry.ProfileId == SelectedRuntime?.Profile.Id;
        InitializeComponent(); DataContext = this;
        TemplatesView.SaveRequested = ApplyTemplatesAsync;
        _timer.Tick += async (_, _) => await RefreshAsync();
        if (initializeRuntime) { Loaded += MainWindow_Loaded; Closing += MainWindow_Closing; }
    }
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var template in await _templates.LoadAsync()) LaunchTemplates.Add(template);
            var profiles = await _profiles.LoadAsync();
            if (profiles.Count == 0) profiles = [new()];
            foreach (var profile in profiles)
            {
                profile.LastProcessId = null; profile.LastProcessStartUtc = null; profile.CollectionEnabled = false;
                if (!LaunchTemplates.Any(template => template.Id == profile.LaunchTemplateId))
                    throw new InvalidDataException($"Нет шаблона для профиля «{profile.Name}». Скопируйте также launch-templates.json.");
                Runtimes.Add(new() { Profile = profile });
            }
            SelectedRuntime = Runtimes.FirstOrDefault();
            TemplatesView.SetTemplates(LaunchTemplates.Where(value => value.Id != Guid.Empty));
            _loaded = true; await SaveProfilesAsync(); _timer.Start(); Log("Лаунчер готов", system: true);
            if (!string.IsNullOrWhiteSpace(StartupProfiles))
                foreach (var id in StartupProfiles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var runtime = Runtimes.FirstOrDefault(value => value.Profile.Id.ToString().Equals(id, StringComparison.OrdinalIgnoreCase) || value.Profile.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (runtime is null) { Log($"Профиль «{id}» не найден", system: true); break; }
                    await LaunchAsync(runtime);
                }
        }
        catch (Exception exception) { Error(exception); }
    }
    private Task SaveProfilesAsync() => _loaded ? _profiles.SaveAsync(Runtimes.Select(value => value.Profile)) : Task.CompletedTask;
    private void SyncPassword()
    {
        if (LaunchPanel?.LoginPasswordBox is null) return;
        _syncingPassword = true;
        try { LaunchPanel.LoginPasswordBox.Password = SelectedRuntime?.Profile.LoginPassword ?? ""; }
        finally { _syncingPassword = false; }
    }
    private void Log(string value, LaunchRuntime? runtime = null, bool system = false)
    {
        var profile = system ? null : (runtime ?? SelectedRuntime)?.Profile;
        Events.Add(new(DateTimeOffset.Now, profile?.Id, profile?.Name ?? "Система", value));
        while (Events.Count > 500) Events.RemoveAt(0);
    }
    private void Error(Exception exception, LaunchRuntime? runtime = null)
    {
        Log(exception.GetBaseException().Message, runtime);
        MessageBox.Show(this, PriceCheck.Collector.RussianUiTextConverter.Translate(exception.GetBaseException().Message),
            "PriceCheck Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
