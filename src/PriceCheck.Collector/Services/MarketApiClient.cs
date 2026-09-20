using System.Net.Http;
using System.Net.Http.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed class MarketApiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly SemaphoreSlim _uploadGate = new(1, 1);

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
        await PostSnapshotAsync(baseUrl, payload, cancellationToken);
    }

    public async Task UploadBrokerAsync(CollectorProfile profile, BrokerInventoryFile inventory, CancellationToken cancellationToken = default)
    {
        var traders = inventory.Rows
            .GroupBy(x => x.TraderObjectId)
            .Select(group =>
            {
                var named = group.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.TraderName));
                var displayName = named?.TraderName.Trim() ?? $"Trader {group.Key}";
                var kioskType = group.First().StoreType;
                var items = group.GroupBy(x => new { x.ItemId, Side = x.StoreType == 3 ? "Buy" : "Sell" })
                    .Select(rows => new
                    {
                        itemId = rows.Key.ItemId,
                        side = rows.Key.Side,
                        quantity = rows.Sum(x => x.Amount),
                        isPackage = rows.Any(x => x.StoreType == 8)
                    }).ToArray();
                return new
                {
                    traderKey = displayName.ToUpperInvariant(), displayName, objectId = group.Key,
                    kioskType, x = (double?)null, y = (double?)null, z = (double?)null,
                    inventoryComplete = true, items
                };
            }).ToArray();
        var payload = new
        {
            batchId = $"broker-{profile.Id:N}-{Guid.NewGuid():N}", sourceId = $"broker:{profile.Id:N}",
            market = profile.Name, city = profile.City, observedAtUtc = inventory.CapturedAtUtc,
            completePresence = false, traders
        };
        var baseUrl = profile.ServerUrl.Trim().TrimEnd('/');
        await PostSnapshotAsync(baseUrl, payload, cancellationToken);
    }

    private async Task PostSnapshotAsync(string baseUrl, object payload, CancellationToken cancellationToken)
    {
        await _uploadGate.WaitAsync(cancellationToken);
        try
        {
            using var response = await _http.PostAsJsonAsync($"{baseUrl}/ingest/market-snapshot", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        finally { _uploadGate.Release(); }
    }

    public void Dispose()
    {
        _uploadGate.Dispose();
        _http.Dispose();
    }
}
