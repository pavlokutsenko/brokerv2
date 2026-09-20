using System.Text.Json.Serialization;

namespace PriceCheck.Collector.Models;

public sealed class BrokerInventoryFile
{
    [JsonPropertyName("captured_at")] public DateTimeOffset CapturedAtUtc { get; init; }
    [JsonPropertyName("elapsed_seconds")] public double ElapsedSeconds { get; init; }
    [JsonPropertyName("summary")] public BrokerInventorySummary Summary { get; init; } = new();
    [JsonPropertyName("rows")] public IReadOnlyList<BrokerInventoryRow> Rows { get; init; } = [];
}

public sealed class BrokerInventorySummary
{
    [JsonPropertyName("unique_traders")] public int UniqueTraders { get; init; }
    [JsonPropertyName("listing_rows")] public int ListingRows { get; init; }
}

public sealed class BrokerInventoryRow
{
    [JsonPropertyName("store_type")] public int StoreType { get; init; }
    [JsonPropertyName("item_id")] public int ItemId { get; init; }
    [JsonPropertyName("trader_object_id")] public int TraderObjectId { get; init; }
    [JsonPropertyName("trader_name")] public string TraderName { get; init; } = "";
    [JsonPropertyName("amount")] public long Amount { get; init; }
}
