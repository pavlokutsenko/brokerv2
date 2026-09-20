using System.Net.Http;
using System.Net.Http.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed class MarketApiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

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

    private static string BaseUrl(CollectorProfile profile) => profile.ServerUrl.Trim().TrimEnd('/');

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var message = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(string.IsNullOrWhiteSpace(message) ? $"Server returned {(int)response.StatusCode}" : message, null, response.StatusCode);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
