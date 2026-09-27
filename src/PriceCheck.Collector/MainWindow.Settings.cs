using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private bool _syncingMarketSelection;
    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        if (Runtimes.Count >= 4) { Log("WARNING: Доступны от 1 до 4 профилей, один профиль на рынок."); return; }
        var name = MarketOptions.FirstOrDefault(market =>
            !Runtimes.Any(runtime => runtime.Profile.Name.Equals(market, StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidOperationException("Все рынки уже настроены.");
        var profile = new CollectorProfile { Name = name };
        var runtime = new ProfileRuntime { Profile = profile };
        Runtimes.Add(runtime);
        ApplySavedGiranCenter();
        SelectedRuntime = runtime;
        await SaveProfilesAsync();
        Log($"Added profile '{profile.Name}'");
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is not { IsBusy: false } runtime || Runtimes.Count <= 1) return;
        var answer = MessageBox.Show($"Delete profile '{runtime.Profile.Name}'?",
            "PriceCheck Collector", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        var index = Runtimes.IndexOf(runtime);
        if (!await StopProfileAsync(runtime)) return;
        Runtimes.Remove(runtime);
        SelectedRuntime = Runtimes.Count == 0 ? null : Runtimes[Math.Clamp(index, 0, Runtimes.Count - 1)];
        await SaveProfilesAsync();
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null) return;
        var currentFile = SelectedRuntime.Profile.LaunchFile;
        var dialog = new OpenFileDialog
        {
            Title = "Lineage 2 client executable",
            Multiselect = false,
            CheckFileExists = true,
            Filter = "Lineage 2 client (*.exe;*.bin)|*.exe;*.bin|All files (*.*)|*.*",
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
        Log($"Client executable: {dialog.FileName}");
    }

    private async void ProfileField_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded || SelectedRuntime is null) return;
        SelectedRuntime.RefreshProfile();
        await SaveProfilesAsync();
    }

    private async void AutoLogin_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || SelectedRuntime is null) return;
        await SaveProfilesAsync();
    }

    private async void LoginPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _syncingLoginPassword || SelectedRuntime is null || sender is not PasswordBox box) return;
        SelectedRuntime.Profile.LoginPassword = box.Password;
        await SaveProfilesAsync();
    }

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
            await _collection.SetCollectionAsync(runtime, false);
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
            if (!runtime.IsCollectionEnabled && Runtimes.Any(other => other != runtime && other.IsCollectionEnabled &&
                other.Profile.Name.Equals(runtime.Profile.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("На этом рынке уже работает другой профиль.");
            if (!runtime.IsCollectionEnabled)
                await _launcher.ValidateProtectionAsync(runtime.Profile.Id, true, CancellationToken.None);
            await _collection.SetCollectionAsync(runtime, !runtime.IsCollectionEnabled);
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
            await _collection.SetCollectionAsync(runtime, false);
            runtime.RefreshProfile();
            await SaveProfilesAsync();
            Log($"{runtime.Profile.Name}: role — {runtime.RoleLabel}");
        }
        catch (Exception exception) { ShowModuleError(runtime, exception); }
        finally { runtime.IsBusy = false; }
    }

    private async void ProfileSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _syncingMarketSelection || SelectedRuntime is null) return;
        var selectionPath = sender is ComboBox selector
            ? System.Windows.Data.BindingOperations.GetBindingExpression(selector, ComboBox.SelectedItemProperty)?.ParentBinding.Path?.Path
            : null;
        var marketChanged = selectionPath == "Profile.Name" && e.RemovedItems.Count > 0 &&
            e.RemovedItems[0] is string previousMarket && previousMarket != SelectedRuntime.Profile.Name;
        if (marketChanged && !SelectedRuntime.CanEditMarket)
        {
            _syncingMarketSelection = true;
            try
            {
                if (e.RemovedItems[0] is string boundMarket)
                    SelectedRuntime.TryChangeMarket(boundMarket, SelectedRuntime.Profile.Name);
                if (sender is ComboBox marketSelector)
                    marketSelector.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateTarget();
            }
            finally { _syncingMarketSelection = false; }
            Log("WARNING: Сначала остановите профиль и отключите его клиент, чтобы изменить рынок.");
            return;
        }
        if (marketChanged && Runtimes.Any(other => other != SelectedRuntime &&
            other.Profile.Name.Equals(SelectedRuntime.Profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is string previous)
                SelectedRuntime.Profile.Name = previous;
            SelectedRuntime.RefreshProfile();
            Log("WARNING: Один рынок может принадлежать только одному профилю.");
            return;
        }
        SelectedRuntime.RefreshProfile();
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
