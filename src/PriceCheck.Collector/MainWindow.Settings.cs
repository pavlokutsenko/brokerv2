using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddServerProfileDialog(this, Runtimes.Select(runtime => runtime.Profile.Name));
        if (dialog.ShowDialog() != true) return;
        var profile = new CollectorProfile
        {
            Name = dialog.ServerName, LoginServerName = dialog.ServerName,
            LoginServerId = dialog.ServerId
        };
        var runtime = new ProfileRuntime { Profile = profile };
        Runtimes.Add(runtime);
        ApplySavedGiranCenter();
        SelectedRuntime = runtime;
        await SaveProfilesAsync();
        Log($"Добавлен профиль «{profile.Name}»");
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _closing || SelectedRuntime is not { IsBusy: false } runtime || Runtimes.Count <= 1) return;
        var answer = MessageBox.Show($"Удалить профиль «{runtime.Profile.Name}»?",
            "PriceCheck Collector", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        await RemoveProfileAsync(runtime);
    }

    private async Task<bool> RemoveProfileAsync(ProfileRuntime runtime)
    {
        if (!_loaded || _closing || runtime.IsBusy || Runtimes.Count <= 1 || !Runtimes.Contains(runtime)) return false;
        var index = Runtimes.IndexOf(runtime);
        if (!await StopProfileAsync(runtime)) return false;
        var wasSelected = SelectedRuntime == runtime;
        Runtimes.Remove(runtime);
        _accountRuntimes.Remove(runtime.Profile.Id);
        _marketActive.Remove(runtime.Profile.Id);
        _lastBrokerOwner.Remove(runtime.Profile.Id);
        if (wasSelected || SelectedRuntime is null)
            SelectedRuntime = Runtimes[Math.Clamp(index, 0, Runtimes.Count - 1)];
        await SaveProfilesAsync();
        Log($"Удалён профиль «{runtime.Profile.Name}»");
        return true;
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null) return;
        var currentFile = SelectedRuntime.Profile.LaunchFile;
        var dialog = new OpenFileDialog
        {
            Title = "Исполняемый файл клиента Lineage 2",
            Multiselect = false,
            CheckFileExists = true,
            Filter = "Клиент Lineage 2 (*.exe;*.bin)|*.exe;*.bin|Все файлы (*.*)|*.*",
            InitialDirectory = File.Exists(currentFile)
                ? Path.GetDirectoryName(currentFile)
                : Directory.Exists(SelectedRuntime.Profile.ClientFolder)
                    ? SelectedRuntime.Profile.ClientFolder
                    : null
        };
        if (dialog.ShowDialog(this) != true) return;
        SelectedRuntime.Profile.LaunchFile = dialog.FileName;
        SelectedRuntime.Profile.ClientFolder = Path.GetDirectoryName(dialog.FileName) ?? "";
        OnPropertyChanged(nameof(SelectedRuntime));
        await SaveProfilesAsync();
        Log($"Исполняемый файл клиента: {dialog.FileName}");
    }

    private async void ProfileField_Changed(object sender, TextChangedEventArgs e)
    {
        if (!CanSaveProfileFields(sender)) return;
        _syncingProfileFields = true;
        try { SelectedRuntime!.RefreshProfile(); }
        finally { _syncingProfileFields = false; }
        await SaveProfilesAsync();
    }

    private bool CanSaveProfileFields(object sender) =>
        _loaded && !_closing && !_syncingProfileFields && SelectedRuntime is not null &&
        sender is FrameworkElement field && ReferenceEquals(field.DataContext, SelectedRuntime);

    private async void MarkCenterZone_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime?.Radar is not RadarSnapshot radar) return;
        SelectedRuntime.Profile.CenterZonesByCity[SelectedRuntime.Profile.City] = new CenterZoneSettings
        {
            X = radar.PlayerX,
            Y = radar.PlayerY
        };
        SelectedRuntime.RefreshProfile();
        await SaveProfilesAsync();
        await RefreshSelectedAsync();
        Log($"{SelectedRuntime.Profile.Name}: center set at ({radar.PlayerX:N0}, {radar.PlayerY:N0})");
    }

    private async void ClearCenterZone_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is not { IsBusy: false } runtime) return;
        runtime.IsBusy = true;
        try
        {
            if(_marketActive.Remove(runtime.Profile.Id,out var active))
                await _collection.SetCollectionAsync(active,false);
            else await _collection.SetCollectionAsync(runtime,false);
            runtime.MarketCollectionEnabled=false;
            runtime.Profile.CenterZonesByCity.Remove(runtime.Profile.City);
            runtime.RefreshProfile();
            await SaveProfilesAsync();
            Log($"{runtime.Profile.Name}: center cleared");
        }
        catch (Exception exception) { ShowModuleError(runtime, exception); }
        finally { runtime.IsBusy = false; }
    }

    private async void ToggleCollection_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is not { IsBusy: false } runtime) return;
        runtime.IsBusy = true;
        try
        {
            if (!_marketActive.ContainsKey(runtime.Profile.Id) && Runtimes.Any(other => other != runtime && other.MarketCollectionEnabled &&
                other.Profile.Name.Equals(runtime.Profile.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("На этом рынке уже работает другой профиль.");
            if(!_marketActive.TryGetValue(runtime.Profile.Id,out var active))
            {
                var accounts=MarketAccounts(runtime);
                active=accounts.FirstOrDefault(r=>r.Session is { } session && ClientProcessIdentity.IsCurrent(session) && r.ClientFault is null)
                    ?? throw new InvalidOperationException("Нет загруженных аккаунтов для этого сервера.");
                foreach(var account in accounts)account.OneTraderPerTurn=accounts.Count(r=>r.Session is { } s && ClientProcessIdentity.IsCurrent(s))>1;
                await _launcher.ValidateProtectionAsync(active.Profile.Id, true, CancellationToken.None);
                await EnsureReaderAttachedAsync(active);
                await _collection.SetCollectionAsync(active,true);
                _marketActive[runtime.Profile.Id]=active;
                _lastBrokerOwner[runtime.Profile.Id]=accounts.ToList().IndexOf(active);
                runtime.MarketCollectionEnabled=true;
                BeginMarketReaderWarmups(runtime);
                foreach(var waiting in accounts.Where(r=>r!=active))
                    _characterRotation.Schedule.Pause(waiting.Profile.Id,DateTimeOffset.UtcNow);
                _characterRotation.Schedule.Resume(active.Profile.Id,DateTimeOffset.UtcNow);
            }
            else
            {
                await StopMarketReaderWarmupsAsync(runtime);
                await _collection.SetCollectionAsync(active,false);
                _marketActive.Remove(runtime.Profile.Id);
                runtime.MarketCollectionEnabled=false;
                foreach(var account in MarketAccounts(runtime))
                    _characterRotation.Schedule.Resume(account.Profile.Id,DateTimeOffset.UtcNow);
            }
            RefreshMarketDisplay(runtime);
            _clientRecovery.Forget(runtime.Profile.Id);
            await SaveProfilesAsync();
        }
        catch (Exception exception) { ShowModuleError(runtime, exception); }
        finally { runtime.IsBusy = false; }
    }
    private async void Role_DropDownClosed(object sender, EventArgs e)
    {
        if (!_loaded || SelectedRuntime is not { IsBusy: false } runtime) return;
        runtime.IsBusy = true;
        try
        {
            if(_marketActive.Remove(runtime.Profile.Id,out var active))
            {
                await StopMarketReaderWarmupsAsync(runtime);
                await _collection.SetCollectionAsync(active,false);
            }
            else await _collection.SetCollectionAsync(runtime,false);
            runtime.MarketCollectionEnabled=false;
            runtime.RefreshProfile();
            await SaveProfilesAsync();
            Log($"{runtime.Profile.Name}: role — {runtime.RoleLabel}");
        }
        catch (Exception exception) { ShowModuleError(runtime, exception); }
        finally { runtime.IsBusy = false; }
    }

    private async void ProfileSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!CanSaveProfileFields(sender)) return;
        var runtime = SelectedRuntime!;
        _syncingProfileFields = true;
        try
        {
            var selectionPath = sender is ComboBox selector
                ? System.Windows.Data.BindingOperations.GetBindingExpression(selector, ComboBox.SelectedItemProperty)?.ParentBinding.Path?.Path
                : null;
            var marketChanged = selectionPath == "Profile.Name" && e.RemovedItems.Count > 0 &&
                e.RemovedItems[0] is string previousMarket && previousMarket != runtime.Profile.Name;
            if (marketChanged && !runtime.CanEditMarket)
            {
                if (e.RemovedItems[0] is string boundMarket)
                    runtime.TryChangeMarket(boundMarket, runtime.Profile.Name);
                if (sender is ComboBox marketSelector)
                    marketSelector.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateTarget();
                Log("WARNING: Сначала остановите профиль и отключите его клиент, чтобы изменить рынок.");
                return;
            }
            if (marketChanged && Runtimes.Any(other => other != runtime &&
                other.Profile.Name.Equals(runtime.Profile.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (e.RemovedItems[0] is string previous) runtime.Profile.Name = previous;
                runtime.RefreshProfile();
                Log("WARNING: Один рынок может принадлежать только одному профилю.");
                return;
            }
            if (marketChanged) runtime.Profile.LoginServerName = runtime.Profile.Name;
            runtime.RefreshProfile();
        }
        finally { _syncingProfileFields = false; }
        await SaveProfilesAsync();
    }

    private void ApplySavedGiranCenter()
    {
        var result = SavedGiranCenter.AssignSharedFallback(Runtimes.Select(runtime => runtime.Profile));
        foreach (var runtime in Runtimes) runtime.RefreshProfile();
        if (result.DistinctSavedCenters > 1 && result.MissingProfiles > 0)
            Log($"WARNING: Конфликт сохранённых центров Гирана: {result.DistinctSavedCenters} вариантов. Для {result.MissingProfiles} профилей центр не выбран.");
        else if (result.ReusedProfiles > 0)
            Log($"INFO: Сохранённый центр Гирана используется ещё в {result.ReusedProfiles} профилях без изменения настроек.");
    }
}
