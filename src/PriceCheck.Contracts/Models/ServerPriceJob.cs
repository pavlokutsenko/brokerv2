namespace PriceCheck.Collector.Models;

public sealed record ServerPriceJob(string TraderId, string TraderKey, string Name, int KioskType,
    double X, double Y, string Revision, string Reason, DateTimeOffset DueAt, string LeaseToken);
public sealed record MarketTaskClaim(bool BrokerAccepted, IReadOnlyList<ServerPriceJob> Jobs, bool AwaitingPreviousBatch = false);
public sealed record MarketTaskRelease(ServerPriceJob Job, string? Error = null);
