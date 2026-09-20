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
            profiles = [new CollectorProfile { Name = "Гиран", Role = CollectorRole.BrokerRadar }];

        foreach (var profile in profiles)
        {
            var runtime = new ProfileRuntime { Profile = profile };
            if (profile.LastProcessId is int pid && _processes.TryClaim(pid))
            {
                runtime.ProcessId = pid;
                runtime.Status = "Подключен после перезапуска UI";
            }
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
        _processes.Release(SelectedRuntime.ProcessId);
        Runtimes.Remove(SelectedRuntime);
        SelectedRuntime = Runtimes.Count == 0 ? null : Runtimes[Math.Clamp(index, 0, Runtimes.Count - 1)];
        await SaveProfilesAsync();
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null) return;
        var dialog = new OpenFolderDialog
        {
            Title = "Папка клиента Lineage 2",
            Multiselect = false,
            InitialDirectory = Directory.Exists(SelectedRuntime.Profile.ClientFolder)
                ? SelectedRuntime.Profile.ClientFolder
                : null
        };
        if (dialog.ShowDialog(this) != true) return;
        SelectedRuntime.Profile.ClientFolder = dialog.FolderName;
        SelectedRuntime.Profile.LaunchFile = null;
        OnPropertyChanged(nameof(SelectedRuntime));
        await SaveProfilesAsync();
        Log($"Папка клиента: {dialog.FolderName}");
    }

    private async void ProfileField_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded || SelectedRuntime is null) return;
        SelectedRuntime.RefreshProfile();
        await SaveProfilesAsync();
    }

    private async void Role_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || SelectedRuntime is null) return;
        SelectedRuntime.RefreshProfile();
        await SaveProfilesAsync();
        Log($"{SelectedRuntime.Profile.Name}: роль — {SelectedRuntime.RoleLabel}");
    }
}

