using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var profiles = await _profileStore.LoadAsync();
        if (profiles.Count == 0)
            profiles = [new CollectorProfile { Name = "Gamma", City = "Giran", Role = CollectorRole.BrokerRadar }];

        foreach (var profile in profiles)
        {
            var runtime = new ProfileRuntime { Profile = profile };
            // A persisted PID cannot prove that our receive hook was installed
            // before world entry. Every application start requires a new owned
            // client lifecycle.
            profile.LastProcessId = null;
            if (!MarketOptions.Contains(profile.Name)) profile.Name = "Gamma";
            if (!CityOptions.Contains(profile.City)) profile.City = "Giran";
            profile.CenterZonesByCity ??= [];
            if (profile.CenterZoneX is double legacyX && profile.CenterZoneY is double legacyY &&
                !profile.CenterZonesByCity.ContainsKey(profile.City))
                profile.CenterZonesByCity[profile.City] = new CenterZoneSettings { X = legacyX, Y = legacyY };
            profile.CenterZoneX = null;
            profile.CenterZoneY = null;
            Runtimes.Add(runtime);
        }
        SelectedRuntime = Runtimes.FirstOrDefault();
        _loaded = true;
        await SaveProfilesAsync();
        _refreshTimer.Start();
        Log("Интерфейс готов");
    }

    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = new CollectorProfile { Name = $"Рынок {Runtimes.Count + 1}" };
        var runtime = new ProfileRuntime { Profile = profile };
        Runtimes.Add(runtime);
        SelectedRuntime = runtime;
        await SaveProfilesAsync();
        Log($"Добавлен профиль «{profile.Name}»");
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null) return;
        var answer = MessageBox.Show($"Удалить профиль «{SelectedRuntime.Profile.Name}»?",
            "PriceCheck Collector", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        var index = Runtimes.IndexOf(SelectedRuntime);
        if (SelectedRuntime.ProcessId is int pid)
        {
            await _radarSessions.StopAsync(pid);
            _processes.Terminate(pid);
        }
        Runtimes.Remove(SelectedRuntime);
        SelectedRuntime = Runtimes.Count == 0 ? null : Runtimes[Math.Clamp(index, 0, Runtimes.Count - 1)];
        await SaveProfilesAsync();
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null) return;
        var currentFile = SelectedRuntime.Profile.LaunchFile;
        var dialog = new OpenFileDialog
        {
            Title = "Файл запуска клиента Lineage 2",
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
        Log($"Файл запуска: {dialog.FileName}");
    }

    private async void ProfileField_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded || SelectedRuntime is null) return;
        SelectedRuntime.RefreshProfile();
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
        Log($"{SelectedRuntime.Profile.Name}: центр зоны отмечен ({radar.PlayerX:N0}, {radar.PlayerY:N0})");
    }

    private async void ClearCenterZone_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null) return;
        SelectedRuntime.IsCollectionEnabled = false;
        SelectedRuntime.Profile.CenterZonesByCity.Remove(SelectedRuntime.Profile.City);
        SelectedRuntime.RefreshProfile();
        await SaveProfilesAsync();
        await RefreshSelectedAsync();
        Log($"{SelectedRuntime.Profile.Name}: центральная зона сброшена");
    }

    private async void ToggleCollection_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null) return;
        if (SelectedRuntime.IsCollectionEnabled)
        {
            SelectedRuntime.IsCollectionEnabled = false;
            await RefreshSelectedAsync();
            Log($"{SelectedRuntime.Profile.Name}: сбор остановлен, каталог заморожен");
            return;
        }
        if (SelectedRuntime.ProcessId is not int || SelectedRuntime.Radar is null) return;
        if (GetCenterZone(SelectedRuntime.Profile) is null)
        {
            MessageBox.Show("Сначала отметьте центральную зону.", "PriceCheck Collector",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedRuntime.IsCollectionEnabled = true;
        await RefreshSelectedAsync();
        Log($"{SelectedRuntime.Profile.Name}: сбор запущен");
    }

    private async void Role_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || SelectedRuntime is null) return;
        SelectedRuntime.RefreshProfile();
        await SaveProfilesAsync();
        Log($"{SelectedRuntime.Profile.Name}: роль — {SelectedRuntime.RoleLabel}");
    }

    private async void ProfileSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || SelectedRuntime is null) return;
        SelectedRuntime.RefreshProfile();
        await SaveProfilesAsync();
    }
}
