namespace PriceCheck.Collector.Models;

public sealed class CenterZoneSettings
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class CollectorProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Gamma";
    public string City { get; set; } = "Giran";
    public CollectorRole Role { get; set; } = CollectorRole.BrokerRadar;
    public string ClientFolder { get; set; } = "";
    public string? LaunchFile { get; set; }
    public int BrokerIntervalMinutes { get; set; } = 5;
    public string ServerUrl { get; set; } = "http://127.0.0.1:3021";
    public Dictionary<string, CenterZoneSettings> CenterZonesByCity { get; set; } = [];
    // Read only during one-time migration from CollectorNext profiles.
    public double? CenterZoneX { get; set; }
    public double? CenterZoneY { get; set; }
    public int? LastProcessId { get; set; }
    public bool CollectionEnabled { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
