using PriceCheck.Collector.Models;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    public Task PrepareMarketReaderAsync(ProfileRuntime runtime,string output,string stopFile)
    {
        if(runtime.Session is not { } session || !_isCurrent(session) || !runtime.ReaderAttached)
            throw new InvalidOperationException("Waiting account has no live reader for read-only preparation.");
        return _worker.RunAsync(session,"market-reader-prepare",output,start=>
            start.Environment["PRICECHECK_STOP_FILE"]=stopFile);
    }
}
