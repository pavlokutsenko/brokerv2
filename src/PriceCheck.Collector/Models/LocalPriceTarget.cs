namespace PriceCheck.Collector.Models;

public sealed record LocalPriceTarget(
    string TraderKey,
    string TraderName,
    long ObjectId,
    int KioskType,
    double X,
    double Y,
    int Priority,
    int AttemptCount);

public sealed record LocalPricePlan(
    LocalPriceTarget Anchor,
    IReadOnlyList<LocalPriceTarget> Batch);
