namespace PriceCheck.Collector.Models;

public enum CollectorRole
{
    BrokerRadar,
    PriceVerifier
}

public sealed record CollectorRoleOption(CollectorRole Value, string Label)
{
    public static IReadOnlyList<CollectorRoleOption> All { get; } =
    [
        new(CollectorRole.BrokerRadar, "Unified collector")
    ];
}
