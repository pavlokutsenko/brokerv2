using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Windows.Storage;

namespace PriceCheck.Collector.Services;

public sealed class LaunchTemplateStore(string? directory = null)
{
    private readonly string _path = Path.Combine(directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PriceCheckCollector"), "launch-templates.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<List<LaunchTemplate>> LoadAsync()
    {
        var templates = DurableJsonFile.ReadRecoverable<List<LaunchTemplate>>(_path) ?? [];
        foreach (var template in templates)
        {
            template.Identity ??= ClientLaunchConfiguration.GenerateIdentity();
            ClientLaunchConfiguration.FillMissingIdentity(template.Identity);
            if (string.IsNullOrEmpty(template.ProxyPasswordProtected)) continue;
            try
            {
                template.ProxyPassword = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    Convert.FromBase64String(template.ProxyPasswordProtected), null, DataProtectionScope.CurrentUser));
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException)
            {
                template.ProxyPassword = "";
                template.ProxyEnabled = false;
            }
        }
        return Task.FromResult(templates.Where(value => value.Id != Guid.Empty).ToList());
    }

    public async Task SaveAsync(IEnumerable<LaunchTemplate> templates)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var values = templates.Where(value => value.Id != Guid.Empty).ToArray();
            foreach (var template in values) ClientResourceBudget.Validate(template);
            foreach (var template in values)
                template.ProxyPasswordProtected = string.IsNullOrEmpty(template.ProxyPassword) ? null :
                    Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(template.ProxyPassword),
                        null, DataProtectionScope.CurrentUser));
            DurableJsonFile.Write(_path, values, new JsonSerializerOptions { WriteIndented = true });
        }
        finally { _gate.Release(); }
    }
}
