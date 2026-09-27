using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class LaunchTemplatesView : UserControl
{
    private bool _changingSelection;
    private List<LaunchTemplate> _savedTemplates = [];
    public ObservableCollection<LaunchTemplate> Templates { get; } = [];
    public ObservableCollection<HardwareScanRow> ScanRows { get; } = [];
    public Func<IReadOnlyList<LaunchTemplate>, Task>? SaveRequested { get; set; }

    public LaunchTemplatesView()
    {
        InitializeComponent();
        DataContext = this;
        Editor.IsEnabled = false;
    }

    public void SetTemplates(IEnumerable<LaunchTemplate> templates)
    {
        _savedTemplates = templates.Select(Clone).ToList();
        Templates.Clear();
        foreach (var template in _savedTemplates.Select(Clone)) Templates.Add(template);
        TemplateList.SelectedIndex = Templates.Count > 0 ? 0 : -1;
    }

    public void RefreshAfterLaunch(IEnumerable<LaunchTemplate> templates)
    {
        if (HasUnsavedChanges()) return;
        var selectedId = (TemplateList.SelectedItem as LaunchTemplate)?.Id;
        SetTemplates(templates);
        TemplateList.SelectedItem = Templates.FirstOrDefault(value => value.Id == selectedId) ?? Templates.FirstOrDefault();
    }

    private bool HasUnsavedChanges() =>
        JsonSerializer.Serialize(Templates) != JsonSerializer.Serialize(_savedTemplates) ||
        !Templates.Select(value => value.ProxyPassword).SequenceEqual(_savedTemplates.Select(value => value.ProxyPassword));

    public void ScrollEditorToTop() => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
        () => EditorScroll.ScrollToTop());

    private static LaunchTemplate Clone(LaunchTemplate value) => new()
    {
        Id = value.Id, Name = value.Name, Description = value.Description,
        HardwareEnabled = value.HardwareEnabled, RotateEachLaunch = value.RotateEachLaunch,
        Identity = new LaunchIdentity
        {
            WorldIdentitySeed = value.Identity.WorldIdentitySeed,
            SystemUuid = value.Identity.SystemUuid, MachineGuid = value.Identity.MachineGuid,
            BoardSerial = value.Identity.BoardSerial, VolumeSerial = value.Identity.VolumeSerial,
            MacAddress = value.Identity.MacAddress,
            SystemSerial = value.Identity.SystemSerial, ChassisSerial = value.Identity.ChassisSerial,
            ProcessorId = value.Identity.ProcessorId, ProcessorSerial = value.Identity.ProcessorSerial,
            MemorySerial = value.Identity.MemorySerial, DiskSerial = value.Identity.DiskSerial,
            HardwareProfileGuid = value.Identity.HardwareProfileGuid,
            WindowsProductId = value.Identity.WindowsProductId,
            DiskGuid = value.Identity.DiskGuid, DiskSignature = value.Identity.DiskSignature,
            SusClientId = value.Identity.SusClientId, VideoIdentifier = value.Identity.VideoIdentifier,
            RegistryComputerName = value.Identity.RegistryComputerName,
            InstallDate = value.Identity.InstallDate, RouterMac = value.Identity.RouterMac
        },
        ProxyEnabled = value.ProxyEnabled, ProxyHost = value.ProxyHost, ProxyPort = value.ProxyPort,
        ProxyUser = value.ProxyUser, ProxyPassword = value.ProxyPassword
    };

    private void TemplateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _changingSelection = true;
        Editor.DataContext = TemplateList.SelectedItem;
        TemplatePassword.Password = (TemplateList.SelectedItem as LaunchTemplate)?.ProxyPassword ?? "";
        _changingSelection = false;
        Editor.IsEnabled = TemplateList.SelectedItem is not null;
        ScrollEditorToTop();
        RefreshPreview();
    }

    private void TemplatePassword_Changed(object sender, RoutedEventArgs e)
    {
        if (!_changingSelection && TemplateList.SelectedItem is LaunchTemplate template)
            template.ProxyPassword = TemplatePassword.Password;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var template = new LaunchTemplate { Identity = ClientLaunchConfiguration.GenerateIdentity() };
        Templates.Add(template);
        TemplateList.SelectedItem = template;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not LaunchTemplate template) return;
        var index = TemplateList.SelectedIndex;
        Templates.Remove(template);
        TemplateList.SelectedIndex = Templates.Count == 0 ? -1 : Math.Min(index, Templates.Count - 1);
    }

    private void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not LaunchTemplate template) return;
        template.Identity = ClientLaunchConfiguration.GenerateIdentity();
        Editor.DataContext = null;
        Editor.DataContext = template;
        RefreshPreview();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Templates.Any(value => string.IsNullOrWhiteSpace(value.Name)) ||
            Templates.GroupBy(value => value.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            MessageBox.Show(Window.GetWindow(this), "Give each template a unique name.",
                "Template name", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            foreach (var template in Templates.Where(value => value.HardwareEnabled))
                ClientLaunchConfiguration.ValidateIdentity(template.Identity);
            foreach (var template in Templates.Where(value => value.ProxyEnabled))
                ClientLaunchConfiguration.ValidateProxy(template);
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "Template settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (SaveRequested is null) return;
        try
        {
            await SaveRequested(Templates.Select(Clone).ToList());
            _savedTemplates = Templates.Select(Clone).ToList();
        }
        catch (Exception exception)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "Could not save templates",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => SetTemplates(_savedTemplates);

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanRows.Clear();
        ScanStatusText.Text = "Scanning…";
        try
        {
            foreach (var row in await Task.Run(HardwareInventoryService.Scan)) ScanRows.Add(row);
            RefreshPreview();
            ScanDetails.IsExpanded = true;
            ScanStatusText.Text = $"Found: {ScanRows.Count}";
        }
        catch (Exception exception)
        {
            ScanStatusText.Text = "Scan failed";
            MessageBox.Show(Window.GetWindow(this), exception.Message, "PC scan",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshPreview()
    {
        var template = TemplateList.SelectedItem as LaunchTemplate;
        var macIndex = 0;
        for (var i = 0; i < ScanRows.Count; i++)
        {
            var row = ScanRows[i];
            ScanRows[i] = IdentityPreviewService.WithTarget(row, template,
                row.Label.StartsWith("MAC · ", StringComparison.Ordinal) ? macIndex++ : 0);
        }
    }
}
