using System.Text.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed class ServerUploadOutbox
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "data", "server-outbox");

    public Task EnqueueRadarAsync(CollectorProfile profile, RadarSnapshot radar)
    {
        var traders = radar.Traders.Where(x => x.IsVisible).Select(x => new
        {
            traderKey = x.Name.Trim().ToUpperInvariant(), displayName = x.Name, objectId = x.ObjectId,
            kioskType = x.KioskType, x = x.X, y = x.Y, z = (double?)null,
            inventoryComplete = false, items = Array.Empty<object>()
        }).ToArray();
        return EnqueueAsync("radar", SnapshotUrl(profile), new
        {
            batchId = $"{profile.Id:N}-{Guid.NewGuid():N}", sourceId = $"collector:{profile.Id:N}",
            market = profile.Name, city = profile.City, observedAtUtc = radar.CapturedAtUtc,
            completePresence = true, traders
        });
    }

    public Task EnqueueBrokerAsync(CollectorProfile profile, BrokerInventoryFile inventory)
    {
        var traders = inventory.Rows.GroupBy(x => x.TraderObjectId).Select(group =>
        {
            var named = group.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.TraderName));
            var displayName = named?.TraderName.Trim() ?? $"Trader {group.Key}";
            var kioskType = group.First().StoreType;
            var items = group.GroupBy(x => new { x.ItemId, Side = x.StoreType == 3 ? "Buy" : "Sell" })
                .Select(rows => new
                {
                    itemId = rows.Key.ItemId, side = rows.Key.Side, quantity = rows.Sum(x => x.Amount),
                    isPackage = rows.Any(x => x.StoreType == 8)
                }).ToArray();
            return new
            {
                traderKey = displayName.ToUpperInvariant(), displayName, objectId = group.Key,
                kioskType, x = (double?)null, y = (double?)null, z = (double?)null,
                inventoryComplete = true, items
            };
        }).ToArray();
        return EnqueueAsync("broker", SnapshotUrl(profile), new
        {
            batchId = $"broker-{profile.Id:N}-{Guid.NewGuid():N}", sourceId = $"broker:{profile.Id:N}",
            market = profile.Name, city = profile.City, observedAtUtc = inventory.CapturedAtUtc,
            completePresence = false, traders
        });
    }

    public Task EnqueueLocalPriceResultAsync(
        CollectorProfile profile, LocalPriceTarget target, ShopCaptureFile capture) =>
        EnqueueAsync("price-snapshot", $"{BaseUrl(profile)}/ingest/price-snapshot", new
        {
            sourceId = $"collector:{profile.Id:N}", market = profile.Name, city = profile.City,
            traderKey = target.TraderKey, displayName = target.TraderName,
            objectId = target.ObjectId, kioskType = target.KioskType,
            side = capture.Side, capturedAtUtc = DateTimeOffset.UtcNow,
            rows = capture.Rows.Select(row => new
            {
                row.RowIndex, row.ItemId, row.ItemObjectId, row.Quantity, row.EnchantLevel,
                row.Price, row.BuyCount, row.BasePrice
            }).ToArray()
        });

    private static async Task EnqueueAsync(string kind, string url, object body)
    {
        Directory.CreateDirectory(DirectoryPath);
        var envelope = new ServerUploadEnvelope
        {
            Kind = kind,
            Url = url,
            Body = JsonSerializer.SerializeToElement(body, Json),
        };
        var name = $"{DateTime.UtcNow:yyyyMMddHHmmssfffffff}-{envelope.Id:N}";
        var temporary = Path.Combine(DirectoryPath, $"{name}.tmp");
        var ready = Path.Combine(DirectoryPath, $"{name}.ready");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(envelope, Json)).ConfigureAwait(false);
        File.Move(temporary, ready);
        UploadWorkerProcess.EnsureRunning();
    }

    private static string SnapshotUrl(CollectorProfile profile) =>
        $"{BaseUrl(profile)}/ingest/market-snapshot";

    private static string BaseUrl(CollectorProfile profile) => profile.ServerUrl.Trim().TrimEnd('/');
}
