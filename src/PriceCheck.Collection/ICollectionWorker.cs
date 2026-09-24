using System.Diagnostics;
using PriceCheck.Contracts;

namespace PriceCheck.Collection;

public interface ICollectionWorker
{
    Task RunAsync(ClientSession session, string mode, string output, Action<ProcessStartInfo> configure);
}
