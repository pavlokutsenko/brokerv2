using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Contracts;

public interface IClientProcessService
{
    bool IsAlive(int pid);
    void Release(int? pid);
    void Terminate(int pid);
    Task<int> LaunchAndBindAsync(CollectorProfile profile, LaunchTemplate? template, CancellationToken cancellationToken);
    Task WaitForGameWindowAsync(int pid, CancellationToken cancellationToken);
    Task ActivateLateAgentAsync(int pid, CancellationToken cancellationToken);
}
