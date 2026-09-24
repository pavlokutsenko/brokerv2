using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = MarketOptions.FirstOrDefault(market =>
            !Runtimes.Any(runtime => runtime.Profile.Name.Equals(market, StringComparison.OrdinalIgnoreCase)))
            ?? $"Client {Runtimes.Count + 1}";
        var profile = new CollectorProfile { Name = name };
        var runtime = new ProfileRuntime { Profile = profile };
        Runtimes.Add(runtime);
        SelectedRuntime = runtime;
        await SaveProfilesAsync();
        Log($"Added profile '{profile.Name}'");
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is not { IsBusy: false } runtime) return;
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
            await _collection.SetCollectionAsync(runtime, !runtime.IsCollectionEnabled);
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
        if (!_loaded || SelectedRuntime is null) return;
        SelectedRuntime.RefreshProfile();
        await SaveProfilesAsync();
    }
}
