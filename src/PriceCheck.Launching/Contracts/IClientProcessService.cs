using PriceCheck.Collector.Models;
using PriceCheck.Contracts;

namespace PriceCheck.Collector.Contracts;

public interface IClientProcessService
{
    bool IsAlive(int pid);
    void Release(int? pid);
    void Terminate(int pid);
    Task<int> LaunchAndBindAsync(CollectorProfile profile, LaunchTemplate? template, CancellationToken cancellationToken);
    Task WaitForGameWindowAsync(int pid, CancellationToken cancellationToken);
    bool TryPlaceGameWindow(ClientSession session, GameWindowCorner corner);
    Task ActivateLateAgentAsync(int pid, CancellationToken cancellationToken);
    PriceCheck.Contracts.ClientProtectionStatus Protection(int pid);
    Task ValidateProtectionAsync(int pid, bool requireWorld, CancellationToken cancellationToken);
    Task ApplyMemoryBudgetAsync(ClientSession session, int maximumMiB, CancellationToken token) =>
        Task.FromException(new NotSupportedException("This process service does not support resident-memory budgets."));
}
