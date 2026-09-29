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
            MessageBox.Show(this, $"Не удалось загрузить настройки: {exception.Message}",
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
                template.Name = "Новый шаблон";
            else if (template.Name.StartsWith("Шаблон ", StringComparison.Ordinal) &&
                     int.TryParse(template.Name["Шаблон ".Length..], out var number))
                template.Name = $"Шаблон {number}";
            LaunchTemplates.Add(template);
        }
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
            // The old UI forced Gamma for every profile. Keep unknown server
            // IDs unconfigured so a new market cannot silently log into Gamma.
            if (profile.Name.Equals("Black", StringComparison.OrdinalIgnoreCase) &&
                profile.LoginServerName.Equals("Gamma", StringComparison.OrdinalIgnoreCase) &&
                profile.LoginServerId == 0)
            {
                profile.LoginServerName = "Black";
            }
            else if (!profile.LoginServerName.Equals(profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                profile.LoginServerName = profile.Name;
                profile.LoginServerId = 0;
            }
            if (GameServerCatalog.TryGetVerifiedId(profile.Name, out var verifiedId) &&
                (profile.LoginServerId == 0 ||
                 profile.Name.Equals("Black", StringComparison.OrdinalIgnoreCase) && profile.LoginServerId == 2))
                profile.LoginServerId = verifiedId;
            if (profile.CharacterSlot is < 0 or > 6) profile.CharacterSlot = 0;
            profile.RotationAccounts ??= [];
            if(profile.RotationAccountIndex<0 || profile.RotationAccountIndex>=profile.RotationAccountCount)
                profile.RotationAccountIndex=0;
            profile.City = "Giran";
            profile.CenterZonesByCity ??= [];
            if (profile.CenterZoneX is double legacyX && profile.CenterZoneY is double legacyY &&
                !profile.CenterZonesByCity.ContainsKey(profile.City))
                profile.CenterZonesByCity[profile.City] = new CenterZoneSettings { X = legacyX, Y = legacyY };
            profile.CenterZoneX = null;
            profile.CenterZoneY = null;
            // A persisted PID is diagnostic history, never a startup action
            // binding. New startup obtains a fresh owned session or an explicit
            // selection from the current process inventory.
            profile.LastProcessId = null;
            profile.LastProcessStartUtc = null;
            profile.CollectionEnabled = false;
            runtime.Status = "Ридер отключён";
            Runtimes.Add(runtime);
        }
        ApplySavedGiranCenter();
        SelectedRuntime = Runtimes.FirstOrDefault();
        _collection.InitializeLocalHistory(Runtimes.Select(runtime => runtime.Profile));
        TemplatesView.SetTemplates(LaunchTemplates.Where(value => value.Id != Guid.Empty));
        _loaded = true;
        await SaveTemplatesAsync();
        await SaveProfilesAsync();
        _refreshTimer.Start();
        Log("Интерфейс готов");
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
                    Log($"Профиль запуска «{names[index]}» не найден");
                    break;
                }
                SelectedRuntime = requested;
                await LaunchProfileAsync(requested, StartupResumeCharacter);
                if (requested.Session is null) break;
            }
        }
        await StartRequestedCollectionAsync();
    }

}
