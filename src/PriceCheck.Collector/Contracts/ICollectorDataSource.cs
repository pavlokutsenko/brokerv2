using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Contracts;

public interface ICollectorDataSource
{
    Task<RadarSnapshot?> ReadRadarAsync(int? expectedPid);
    Task<BrokerSnapshot?> ReadLatestBrokerAsync();
}

