using System.Text.Json.Serialization;

namespace PriceCheck.Collector.Models;

public sealed class PriceQueueJob
{
    public required string TraderId { get; init; }
    public required string TraderName { get; init; }
    public required string TraderKey { get; init; }
    public required string LeaseToken { get; init; }
    public string? ObjectId { get; init; }
    public int KioskType { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Z { get; init; }
    public int AttemptCount { get; init; }
}

public sealed class ShopCaptureFile
{
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
