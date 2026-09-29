using System.Windows;
using System.Windows.Controls;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async void LaunchTemplateSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!CanSaveProfileFields(sender)) return;
        OnPropertyChanged(nameof(SelectedTemplateSummary));
        await SaveProfilesAsync();
    }

    private void ManageTemplates_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedItem = TemplatesTab;

    private void SidebarProfileSelected(object sender, RoutedEventArgs e) => MainTabs.SelectedItem = ProfilesTab;

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;
        SidebarView.IsTemplatesSelected = ReferenceEquals(MainTabs.SelectedItem, TemplatesTab);
        if (ReferenceEquals(MainTabs.SelectedItem, TemplatesTab)) TemplatesView?.ScrollEditorToTop();
    }

    private async Task ApplyTemplatesAsync(IReadOnlyList<LaunchTemplate> updated)
    {
        LaunchTemplates.Clear();
        LaunchTemplates.Add(new LaunchTemplate { Id = Guid.Empty, Name = "Без шаблона", HardwareEnabled = false });
        foreach (var template in updated) LaunchTemplates.Add(template);
        foreach (var runtime in Runtimes)
            if (!updated.Any(value => value.Id == runtime.Profile.LaunchTemplateId))
                runtime.Profile.LaunchTemplateId = Guid.Empty;
        OnPropertyChanged(nameof(SelectedRuntime));
        OnPropertyChanged(nameof(SelectedTemplateSummary));
        await SaveTemplatesAsync();
        await SaveProfilesAsync();
    }

    private Task SaveTemplatesAsync() => _templateStore.SaveAsync(LaunchTemplates);

    private static string ImportedTemplateName(string name, bool hardware, bool proxy) =>
        name + " · " + (hardware ? proxy ? "ротация HWID + HTTP-прокси" : "ротация HWID" : "HTTP-прокси");
}
