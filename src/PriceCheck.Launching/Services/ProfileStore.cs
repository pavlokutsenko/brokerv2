using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Contracts;
using PriceCheck.Windows.Storage;

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

    public ProfileStore(string? directory = null)
    {
        var root = directory ?? Path.Combine(
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
        if (directory is null && !File.Exists(_path) && !File.Exists(_path+".bak") && !File.Exists(_path+".tmp"))
        {
            if (File.Exists(portablePath)) File.Copy(portablePath, _path);
            else if (File.Exists(legacyPath)) File.Copy(legacyPath, _path);
        }
    }

    public Task<IReadOnlyList<CollectorProfile>> LoadAsync()
    {
        var profiles = DurableJsonFile.ReadRecoverable<List<CollectorProfile>>(_path, JsonOptions, Validate) ?? [];
        foreach (var profile in profiles)
        {
            profile.City = "Giran";
            profile.RotationAccounts ??= [];
            if(profile.RotationAccountIndex<0 || profile.RotationAccountIndex>=profile.RotationAccountCount)
                profile.RotationAccountIndex=0;
            if (profile.LoginServerId == 0 &&
                GameServerCatalog.TryGetVerifiedId(profile.LoginServerName, out var serverId))
                profile.LoginServerId = serverId;
            profile.ProxyPassword = Unprotect(profile.ProxyPasswordProtected, () => profile.ProxyEnabled = false);
            profile.LoginPassword = Unprotect(profile.LoginPasswordProtected, () => profile.AutoLoginEnabled = false);
            foreach(var account in profile.RotationAccounts)
                account.LoginPassword=Unprotect(account.LoginPasswordProtected,()=>profile.CharacterRotationEnabled=false);
        }
        SavedGiranCenter.AssignSharedFallback(profiles);
        return Task.FromResult<IReadOnlyList<CollectorProfile>>(profiles);
    }

    public async Task SaveAsync(IEnumerable<CollectorProfile> profiles)
    {
        await _saveGate.WaitAsync();
        try
        {
            var materialized = profiles.ToArray();
            if (!Validate(materialized)) throw new InvalidDataException("Profile configuration is not valid; previous settings were preserved.");
            foreach (var profile in materialized)
            {
                profile.City = "Giran";
                profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
                profile.ProxyPasswordProtected = string.IsNullOrEmpty(profile.ProxyPassword) ? null :
                    Protect(profile.ProxyPassword);
                profile.LoginPasswordProtected = string.IsNullOrEmpty(profile.LoginPassword) ? null :
                    Protect(profile.LoginPassword);
                foreach(var account in profile.RotationAccounts)
                    if(!string.IsNullOrEmpty(account.LoginPassword))
                        account.LoginPasswordProtected=Protect(account.LoginPassword);
            }
            DurableJsonFile.Write(_path, materialized, JsonOptions);
        }
        finally { _saveGate.Release(); }
    }

    private static string Protect(string value) => Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    private static bool Validate(IReadOnlyCollection<CollectorProfile> profiles) =>
        profiles.All(profile => profile is not null && profile.Id != Guid.Empty && !string.IsNullOrWhiteSpace(profile.Name) &&
            double.IsFinite(profile.RecheckHours) && profile.RecheckHours is >= 0.1 and <= 8760 &&
            profile.TraderPauseSeconds is >= 0 and <= 3600 &&
            profile.LoginServerId is >= 0 and <= 1000 &&
            profile.RotationAccounts is {Count: <=20} accounts &&
            profile.RotationAccountIndex>=0 && profile.RotationAccountIndex<=accounts.Count &&
            accounts.All(account=>account is not null && account.Id!=Guid.Empty &&
                !string.IsNullOrWhiteSpace(account.LoginName) && account.LoginName.Length<=120 &&
                (!string.IsNullOrEmpty(account.LoginPassword) || !string.IsNullOrEmpty(account.LoginPasswordProtected))) &&
            accounts.Select(account=>account.Id).Distinct().Count()==accounts.Count &&
            (accounts.Count==0 || !string.IsNullOrWhiteSpace(profile.LoginName)) &&
            accounts.Select(account=>account.LoginName.Trim()).Append(profile.LoginName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Count()==accounts.Count+1) &&
        profiles.Select(profile => profile.Id).Distinct().Count() == profiles.Count;

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
