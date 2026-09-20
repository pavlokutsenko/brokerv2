namespace PriceCheck.Collector.Models;

public sealed record RadarPoint(
    int ObjectId,
    string Name,
    int KioskType,
    double X,
    double Y,
    double Distance);

public sealed class RadarSnapshot
{
    public int ProcessId { get; init; }
    public double PlayerX { get; init; }
    public double PlayerY { get; init; }
    public int PositionedActors { get; init; }
    public IReadOnlyList<RadarPoint> Traders { get; init; } = [];
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

