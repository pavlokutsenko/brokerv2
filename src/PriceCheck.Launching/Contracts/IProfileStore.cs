using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Contracts;

public interface IProfileStore
{
    Task<IReadOnlyList<CollectorProfile>> LoadAsync();
    Task SaveAsync(IEnumerable<CollectorProfile> profiles);
}

