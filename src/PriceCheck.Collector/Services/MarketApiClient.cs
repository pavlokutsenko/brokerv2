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
            await EnsureSuccessAsync(response, cancellationToken);
        }
        finally { _uploadGate.Release(); }
    }

    public async Task<PriceQueueJob?> ClaimPriceJobAsync(CollectorProfile profile, RadarSnapshot radar, string workerId, CancellationToken cancellationToken = default)
    {
        var payload = new { market = profile.Name, city = profile.City, workerId, currentX = radar.PlayerX, currentY = radar.PlayerY, leaseSeconds = 120 };
        using var response = await _http.PostAsJsonAsync($"{BaseUrl(profile)}/v1/price-check-queue/claim", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PriceQueueJob>(cancellationToken: cancellationToken);
    }

    public async Task HeartbeatPriceJobAsync(CollectorProfile profile, PriceQueueJob job, string workerId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync($"{BaseUrl(profile)}/v1/price-check-queue/{job.TraderId}/heartbeat", new { workerId, leaseToken = job.LeaseToken, leaseSeconds = 120 }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SubmitPriceJobAsync(CollectorProfile profile, PriceQueueJob job, ShopCaptureFile capture, string workerId, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            workerId, leaseToken = job.LeaseToken, side = capture.Side, capturedAtUtc = DateTimeOffset.UtcNow,
            rows = capture.Rows.Select(row => new { row.RowIndex, row.ItemId, row.ItemObjectId, row.Quantity, row.EnchantLevel, row.Price, row.BuyCount, row.BasePrice }).ToArray()
        };
        using var response = await _http.PostAsJsonAsync($"{BaseUrl(profile)}/v1/price-check-queue/{job.TraderId}/result", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task FailPriceJobAsync(CollectorProfile profile, PriceQueueJob job, string workerId, string error, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync($"{BaseUrl(profile)}/v1/price-check-queue/{job.TraderId}/fail", new { workerId, leaseToken = job.LeaseToken, error }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static string BaseUrl(CollectorProfile profile) => profile.ServerUrl.Trim().TrimEnd('/');

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var message = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(string.IsNullOrWhiteSpace(message) ? $"Server returned {(int)response.StatusCode}" : message, null, response.StatusCode);
    }

    public void Dispose()
    {
        _uploadGate.Dispose();
        _http.Dispose();
    }
}
