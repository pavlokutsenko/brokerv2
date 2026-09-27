using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PriceCheck.Collector.Models;

namespace PriceCheck.Launcher;

public partial class MainWindow
{
    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _closing) return;
        var runtime = new LaunchRuntime { Profile = new() };
        Runtimes.Add(runtime); SelectedRuntime = runtime;
        await SaveFieldsAsync();
    }
    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _closing || SelectedRuntime is not { IsBusy: false } runtime || Runtimes.Count <= 1) return;
        if (MessageBox.Show(this, $"Удалить профиль '{runtime.Profile.Name}'?", "PriceCheck Launcher", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            await StopAsync(runtime); Runtimes.Remove(runtime);
            SelectedRuntime = Runtimes.FirstOrDefault(); await SaveProfilesAsync();
        }
        catch (Exception exception) { Error(exception); }
    }
    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is not { IsBusy: false } runtime) return;
        var dialog = new OpenFileDialog { Title = "Lineage 2 client", Filter = "Client (*.exe;*.bin)|*.exe;*.bin|All files|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        runtime.Profile.LaunchFile = dialog.FileName;
        runtime.Profile.ClientFolder = Path.GetDirectoryName(dialog.FileName) ?? "";
        runtime.RefreshProfile(); await SaveFieldsAsync();
    }
    private async void ProfileField_Changed(object sender, TextChangedEventArgs e) => await SaveFieldsAsync();
    private async void AutoLogin_Changed(object sender, RoutedEventArgs e) => await SaveFieldsAsync();
    private async void ProfileSelection_Changed(object sender, SelectionChangedEventArgs e) => await SaveFieldsAsync();
    private async void LoginPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _syncingPassword || SelectedRuntime is null || sender is not PasswordBox box) return;
        SelectedRuntime.Profile.LoginPassword = box.Password;
        await SaveFieldsAsync();
    }
    private async Task SaveFieldsAsync()
    {
        if (!_loaded || _closing) return;
        try
        {
            SelectedRuntime?.RefreshProfile(); Changed(nameof(SelectedTemplateSummary));
            await SaveProfilesAsync();
        }
        catch (Exception exception) { Error(exception); }
    }
    private void ManageTemplates_Click(object sender, RoutedEventArgs e)
    { MainTabs.SelectedItem = TemplatesTab; TemplatesView.ScrollEditorToTop(); }
    private async Task ApplyTemplatesAsync(IReadOnlyList<LaunchTemplate> values)
    {
        await _templates.SaveAsync(values);
        LaunchTemplates.Clear();
        LaunchTemplates.Add(new() { Id = Guid.Empty, Name = "No template", HardwareEnabled = false });
        foreach (var template in values) LaunchTemplates.Add(template);
        foreach (var runtime in Runtimes)
            if (!LaunchTemplates.Any(value => value.Id == runtime.Profile.LaunchTemplateId)) runtime.Profile.LaunchTemplateId = Guid.Empty;
        Changed(nameof(SelectedTemplateSummary)); Changed(nameof(SelectedRuntime));
        await SaveProfilesAsync();
    }
    private async Task SaveRotatedTemplateAsync(LaunchTemplate? template)
    {
        if (template?.RotateEachLaunch != true) return;
        await _templates.SaveAsync(LaunchTemplates);
        TemplatesView.RefreshAfterLaunch(LaunchTemplates.Where(value => value.Id != Guid.Empty));
    }
}
