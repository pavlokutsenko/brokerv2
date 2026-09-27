using PriceCheck.Collector.Models;

namespace PriceCheck.Contracts;

public interface IMarketTaskClient
{
    Task<MarketCycleStatus> StateAsync(CollectorProfile profile);
    Task<MarketTaskClaim> ClaimAsync(CollectorProfile profile, string workerId, string batchId,
        string requestId, IReadOnlyList<string> keys, double x, double y);
    Task<IReadOnlyList<string>> RenewAsync(CollectorProfile profile, string workerId, IReadOnlyList<ServerPriceJob> jobs);
    Task ReleaseAsync(CollectorProfile profile, string workerId, IReadOnlyList<MarketTaskRelease> jobs);
}
