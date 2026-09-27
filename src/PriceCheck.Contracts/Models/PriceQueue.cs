using System.Text.Json.Serialization;

namespace PriceCheck.Collector.Models;

public sealed class ShopCaptureFile
{
    public DateTimeOffset? ReadStartedAtUtc { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string SnapshotId { get; init; } = Guid.NewGuid().ToString("N");
    public string Precision { get; init; } = "client_int32_unverified";
    [JsonPropertyName("side")] public string Side { get; init; } = "sell";
    [JsonPropertyName("rows")] public IReadOnlyList<ShopCaptureRow> Rows { get; init; } = [];
}

public sealed class ShopCaptureRow
{
    [JsonPropertyName("row_index")] public int RowIndex { get; init; }
    [JsonPropertyName("item_object_id")] public long ItemObjectId { get; init; }
    [JsonPropertyName("item_id")] public long ItemId { get; init; }
    [JsonPropertyName("count")] public long Quantity { get; init; }
    [JsonPropertyName("enchant_level")] public int EnchantLevel { get; init; }
    [JsonPropertyName("price")] public long Price { get; init; }
    [JsonPropertyName("buy_count")] public long BuyCount { get; init; }
    [JsonPropertyName("base_price")] public long BasePrice { get; init; }
}

public sealed class PriceBatchCaptureFile
{
    [JsonPropertyName("shops")] public IReadOnlyList<PriceBatchShopCapture> Shops { get; init; } = [];
    [JsonPropertyName("failures")] public IReadOnlyList<PriceBatchFailure> Failures { get; init; } = [];
    [JsonPropertyName("elapsed_seconds")] public double ElapsedSeconds { get; init; }
    [JsonPropertyName("shops_per_second")] public double ShopsPerSecond { get; init; }
}

public sealed class PriceBatchShopCapture
{
    [JsonPropertyName("trader")] public PriceBatchTrader Trader { get; init; } = new();
    [JsonPropertyName("side")] public string Side { get; init; } = "sell";
    [JsonPropertyName("rows")] public IReadOnlyList<ShopCaptureRow> Rows { get; init; } = [];
}

public sealed class PriceBatchTrader
{
    [JsonPropertyName("object_id")] public long ObjectId { get; init; }
}

public sealed class PriceBatchFailure
{
    [JsonPropertyName("object_id")] public long ObjectId { get; init; }
    [JsonPropertyName("error")] public string Error { get; init; } = "Shop did not respond";
}
