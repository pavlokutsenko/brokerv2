using PriceCheck.Collector.Models;

namespace PriceCheck.Collection;

public interface IRadarSessions : IAsyncDisposable
{
    Task StartAsync(int pid, MarketZone? zone, bool collectionEnabled, CancellationToken cancellationToken);
    RadarSnapshot? Snapshot(int pid, MarketZone? zone, bool collectionEnabled);
    Task StopAsync(int pid);
}
