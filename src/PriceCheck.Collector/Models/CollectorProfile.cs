namespace PriceCheck.Collector.Models;

public sealed class CollectorProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Gamma";
    public string City { get; set; } = "Giran";
    public CollectorRole Role { get; set; } = CollectorRole.BrokerRadar;
    public string ClientFolder { get; set; } = "";
    public string? LaunchFile { get; set; }
    public int BrokerIntervalMinutes { get; set; } = 5;
    public int? LastProcessId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
