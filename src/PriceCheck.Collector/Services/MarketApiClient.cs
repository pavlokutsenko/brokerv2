using System.Net.Http;
using System.Net.Http.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed class MarketApiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };

    public async Task UploadRadarAsync(CollectorProfile profile, RadarSnapshot radar, CancellationToken cancellationToken = default)
    {
        var traders = radar.Traders.Where(x => x.IsVisible).Select(x => new
        {
            traderKey = x.Name.Trim().ToUpperInvariant(), displayName = x.Name, objectId = x.ObjectId,
            kioskType = x.KioskType, x = x.X, y = x.Y, z = (double?)null,
            inventoryComplete = false, items = Array.Empty<object>()
        }).ToArray();
        var payload = new
        {
            batchId = $"{profile.Id:N}-{Guid.NewGuid():N}", sourceId = $"collector:{profile.Id:N}",
            market = profile.Name, city = profile.City, observedAtUtc = radar.CapturedAtUtc,
            completePresence = true, traders
        };
        var baseUrl = profile.ServerUrl.Trim().TrimEnd('/');
        using var response = await _http.PostAsJsonAsync($"{baseUrl}/ingest/market-snapshot", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
