using System.Text.Json.Serialization;

namespace PriceCheck.Collector.Models;

public sealed class BrokerInventoryFile
{
    [JsonPropertyName("native_state_observations")] public IReadOnlyList<BrokerNativeStateObservation> NativeStateObservations { get; init; } = [];
    [JsonPropertyName("warnings")] public IReadOnlyList<string> Warnings { get; init; } = [];
    [JsonPropertyName("binding_pid")] public int BindingPid { get; init; }
    [JsonPropertyName("bindings")] public IReadOnlyList<BrokerActorBinding> Bindings { get; init; } = [];
    [JsonPropertyName("complete")] public bool Complete { get; init; }
    [JsonPropertyName("started_at")] public DateTimeOffset StartedAtUtc { get; init; }
    [JsonPropertyName("captured_at")] public DateTimeOffset CapturedAtUtc { get; init; }
    [JsonPropertyName("elapsed_seconds")] public double ElapsedSeconds { get; init; }
    [JsonPropertyName("summary")] public BrokerInventorySummary Summary { get; init; } = new();
    [JsonPropertyName("rows")] public IReadOnlyList<BrokerInventoryRow> Rows { get; init; } = [];
}

public sealed class BrokerNativeStateObservation
{
    [JsonPropertyName("object_id")] public int ObjectId { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("kiosk_type")] public int KioskType { get; init; }
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
    [JsonPropertyName("observed_at")] public DateTimeOffset ObservedAt { get; init; }
    [JsonPropertyName("collector_x")] public double CollectorX { get; init; }
    [JsonPropertyName("collector_y")] public double CollectorY { get; init; }
}

public sealed class BrokerActorBinding
{
    [JsonPropertyName("object_id")] public int ObjectId { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("kiosk_type")] public int KioskType { get; init; }
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
}

public sealed class BrokerInventorySummary
{
    [JsonPropertyName("market_item_requests")] public int ItemRequests { get; init; }
    [JsonPropertyName("market_item_responses")] public int ItemResponses { get; init; }
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
