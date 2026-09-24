using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Contracts;

namespace PriceCheck.Collector.Services;

public sealed class ProfileStore : IProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public ProfileStore()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheckCollector");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "profiles.json");
        var portablePath = Path.Combine(AppContext.BaseDirectory, "profiles.json");
        var legacyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheck",
            "CollectorNext");
        var legacyPath = Path.Combine(legacyRoot, "profiles.json");
        if (!File.Exists(_path) && File.Exists(portablePath)) File.Copy(portablePath, _path);
        if (!File.Exists(_path) && File.Exists(legacyPath)) File.Copy(legacyPath, _path);
    }

    public async Task<IReadOnlyList<CollectorProfile>> LoadAsync()
    {
        if (!File.Exists(_path)) return [];
        await using var stream = File.OpenRead(_path);
        var profiles = await JsonSerializer.DeserializeAsync<List<CollectorProfile>>(stream, JsonOptions) ?? [];
        foreach (var profile in profiles)
        {
            profile.ProxyPassword = Unprotect(profile.ProxyPasswordProtected, () => profile.ProxyEnabled = false);
            profile.LoginPassword = Unprotect(profile.LoginPasswordProtected, () => profile.AutoLoginEnabled = false);
        }
        return profiles;
    }

    public async Task SaveAsync(IEnumerable<CollectorProfile> profiles)
    {
        await _saveGate.WaitAsync();
        try
        {
            var materialized = profiles.ToArray();
            foreach (var profile in materialized)
            {
                profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
                profile.ProxyPasswordProtected = string.IsNullOrEmpty(profile.ProxyPassword) ? null :
                    Protect(profile.ProxyPassword);
                profile.LoginPasswordProtected = string.IsNullOrEmpty(profile.LoginPassword) ? null :
                    Protect(profile.LoginPassword);
            }
            var temporary = _path + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, materialized, JsonOptions);
            }
            File.Move(temporary, _path, true);
        }
        finally { _saveGate.Release(); }
    }

    private static string Protect(string value) => Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    private static string Unprotect(string? value, Action onFailure)
    {
        if (string.IsNullOrEmpty(value)) return "";
        try
        {
            var encrypted = Convert.FromBase64String(value);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            onFailure();
            return "";
        }
    }
}
