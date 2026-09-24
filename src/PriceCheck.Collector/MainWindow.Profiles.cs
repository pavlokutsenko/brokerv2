using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<CollectorProfile> profiles;
        List<LaunchTemplate> storedTemplates;
        try
        {
            profiles = await _profileStore.LoadAsync();
            storedTemplates = await _templateStore.LoadAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not load settings: {exception.Message}",
                "PriceCheck Collector", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        foreach (var template in storedTemplates)
        {
            if (template.Name.EndsWith(" · прежний запуск", StringComparison.Ordinal))
                template.Name = ImportedTemplateName(template.Name[..^" · прежний запуск".Length],
                    template.HardwareEnabled, template.ProxyEnabled);
            else if (template.Name.EndsWith(" · случайный HWID", StringComparison.Ordinal))
                template.Name = ImportedTemplateName(template.Name[..^" · случайный HWID".Length],
                    template.HardwareEnabled, template.ProxyEnabled);
            else if (template.Name == "Новый шаблон")
                template.Name = "New template";
            else if (template.Name.StartsWith("Шаблон ", StringComparison.Ordinal) &&
                     int.TryParse(template.Name["Шаблон ".Length..], out var number))
                template.Name = $"Template {number}";
            LaunchTemplates.Add(template);
        }
        var restoredPids = new HashSet<int>();
        if (profiles.Count == 0)
            profiles = [new CollectorProfile { Name = "Gamma", City = "Giran", Role = CollectorRole.BrokerRadar }];
        foreach (var profile in profiles)
        {
            if (profile.LaunchTemplateId == Guid.Empty && (profile.GenerateHardwareIdentity || profile.ProxyEnabled))
            {
                var migrated = new LaunchTemplate
                {
                    Name = ImportedTemplateName(profile.Name, profile.GenerateHardwareIdentity, profile.ProxyEnabled),
                    HardwareEnabled = profile.GenerateHardwareIdentity,
                    RotateEachLaunch = profile.GenerateHardwareIdentity,
                    Identity = ClientLaunchConfiguration.GenerateIdentity(),
                    ProxyEnabled = profile.ProxyEnabled, ProxyHost = profile.ProxyHost,
                    ProxyPort = profile.ProxyPort, ProxyUser = profile.ProxyUser,
                    ProxyPassword = profile.ProxyPassword
                };
                LaunchTemplates.Add(migrated);
                profile.LaunchTemplateId = migrated.Id;
                profile.GenerateHardwareIdentity = false;
                profile.ProxyEnabled = false;
                profile.ProxyHost = "";
                profile.ProxyPort = 0;
                profile.ProxyUser = "";
                profile.ProxyPassword = "";
                profile.ProxyPasswordProtected = null;
            }
            if (!LaunchTemplates.Any(value => value.Id == profile.LaunchTemplateId))
                profile.LaunchTemplateId = Guid.Empty;
            profile.Role = CollectorRole.BrokerRadar;
            var runtime = new ProfileRuntime { Profile = profile };
            if (!LoginServerOptions.Contains(profile.LoginServerName)) profile.LoginServerName = "Gamma";
            if (profile.CharacterSlot is < 0 or > 6) profile.CharacterSlot = 0;
            if (!CityOptions.Contains(profile.City)) profile.City = "Giran";
            profile.CenterZonesByCity ??= [];
            if (profile.CenterZoneX is double legacyX && profile.CenterZoneY is double legacyY &&
                !profile.CenterZonesByCity.ContainsKey(profile.City))
                profile.CenterZonesByCity[profile.City] = new CenterZoneSettings { X = legacyX, Y = legacyY };
            profile.CenterZoneX = null;
            profile.CenterZoneY = null;
            // Restore only a verified process reference, never reader hooks or proxy ownership.
            if (profile.LastProcessId is int pid && profile.LastProcessStartUtc is DateTimeOffset started &&
                PriceCheck.Windows.ClientProcessIdentity.Read(pid) is { } session &&
                session.StartedAtUtc == started && restoredPids.Add(pid))
            {
                runtime.Session = session;
                runtime.LaunchStatus = "Existing client · managed elsewhere";
            }
            else { profile.LastProcessId = null; profile.LastProcessStartUtc = null; }
            profile.CollectionEnabled = false;
            runtime.Status = "Reader disconnected";
            Runtimes.Add(runtime);
        }
        SelectedRuntime = Runtimes.FirstOrDefault();
        TemplatesView.SetTemplates(LaunchTemplates.Where(value => value.Id != Guid.Empty));
        _loaded = true;
        await SaveTemplatesAsync();
        await SaveProfilesAsync();
        RefreshClientList();
        _refreshTimer.Start();
        Log("Interface ready");
        if (!string.IsNullOrWhiteSpace(StartupLaunchProfileName))
        {
            var names = StartupLaunchProfileName.Split(',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < names.Length; ++index)
            {
                var requested = Runtimes.FirstOrDefault(runtime =>
                    runtime.Profile.Name.Equals(names[index], StringComparison.OrdinalIgnoreCase));
                if (requested is null)
                {
                    Log($"Startup profile '{names[index]}' was not found");
                    break;
                }
                SelectedRuntime = requested;
                await LaunchProfileAsync(requested);
                if (requested.Session is null) break;
            }
        }
    }

}
