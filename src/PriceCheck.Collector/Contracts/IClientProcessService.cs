using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Contracts;

public interface IClientProcessService
{
    bool IsAlive(int pid);
    bool TryClaim(int pid);
    void Release(int? pid);
    Task<int> LaunchAndBindAsync(CollectorProfile profile, CancellationToken cancellationToken);
    int AttachNewestUnclaimed();
}

