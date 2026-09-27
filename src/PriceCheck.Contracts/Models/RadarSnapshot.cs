namespace PriceCheck.Collector.Models;

public readonly record struct MarketZone(double X, double Y, double Radius);

public sealed record RadarPoint(
    int ObjectId,
    string Name,
    int KioskType,
    double X,
    double Y,
    double Distance,
    bool IsVisible,
    DateTimeOffset LastSeenAtUtc)
{
    // State observation is independent of movement/visibility updates.
    // These timestamps belong to this reader session, never to an ObjectID identity.
    public DateTimeOffset StateObservedAtUtc { get; init; }
    public double? StateObservedPlayerX { get; init; }
    public double? StateObservedPlayerY { get; init; }
    public DateTimeOffset? LastClosedAtUtc { get; init; }
    public DateTimeOffset? LastReopenedAtUtc { get; init; }
    public long TradeRevision { get; init; }
}

public sealed class RadarSnapshot
{
    public int ProcessId { get; init; }
    public bool LivePlayerPositionAvailable { get; init; }
    public bool WorldCharacterDataAvailable { get; init; } = true;
    public double PlayerX { get; init; }
    public double PlayerY { get; init; }
    public bool CenterZoneConfigured { get; init; }
    public bool IsInsideCenterZone { get; init; }
    public bool CollectionRequested { get; init; }
    public double CenterZoneX { get; init; }
    public double CenterZoneY { get; init; }
    public double CenterZoneRadius { get; init; }
    public int PositionedActors { get; init; }
    public int VisibleTraders { get; init; }
    public IReadOnlyList<RadarPoint> Traders { get; init; } = [];
    // Current-process character identities, independent of shop/visibility state.
    // Only broker-returned ObjectIDs may use these to name inventory rows.
    public IReadOnlyList<RadarPoint> BrokerIdentities { get; init; } = [];
    // Explicit character-info kiosk=0 for a previously trading character.
    // Visibility loss is not a shop closure.
    public IReadOnlyList<RadarPoint> ClosedTraders { get; init; } = [];
    public DateTimeOffset CapturedAtUtc { get; init; }
}

public sealed class BrokerSnapshot
{
    public int UniqueTraders { get; init; }
    public int ListingRows { get; init; }
    public int SellTraders { get; init; }
    public int BuyTraders { get; init; }
    public int PackageTraders { get; init; }
    public double ElapsedSeconds { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
}
