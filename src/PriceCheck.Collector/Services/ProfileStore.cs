using System.Text.Json;
using System.Text.Json.Serialization;
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
        _path = Path.Combine(AppContext.BaseDirectory, "profiles.json");
        var legacyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheck",
            "CollectorNext");
        var legacyPath = Path.Combine(legacyRoot, "profiles.json");
        if (!File.Exists(_path) && File.Exists(legacyPath)) File.Copy(legacyPath, _path);
    }

    public async Task<IReadOnlyList<CollectorProfile>> LoadAsync()
    {
        if (!File.Exists(_path)) return [];
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<CollectorProfile>>(stream, JsonOptions) ?? [];
    }

    public async Task SaveAsync(IEnumerable<CollectorProfile> profiles)
    {
        await _saveGate.WaitAsync();
        try
        {
            var materialized = profiles.ToArray();
            foreach (var profile in materialized) profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
            var temporary = _path + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, materialized, JsonOptions);
            }
            File.Move(temporary, _path, true);
        }
        finally { _saveGate.Release(); }
    }
}
