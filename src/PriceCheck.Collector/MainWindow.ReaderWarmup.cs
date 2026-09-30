using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private sealed record ReaderWarmup(int ProcessId,string StopFile,Task Work);
    private readonly Dictionary<Guid,ReaderWarmup> _readerWarmups=[];
    private readonly HashSet<Guid> _stoppedWarmupMarkets=[];

    private void BeginMarketReaderWarmups(ProfileRuntime root) => _stoppedWarmupMarkets.Remove(root.Profile.Id);

    private void EnsureMarketReaderWarmups(ProfileRuntime root)
    {
        if(_stoppedWarmupMarkets.Contains(root.Profile.Id) || !root.MarketCollectionEnabled ||
           !_marketActive.TryGetValue(root.Profile.Id,out var active))return;
        foreach(var waiting in MarketAccounts(root).Where(account=>account!=active && account.ReaderAttached &&
                    account.Session is { } session && ClientProcessIdentity.IsCurrent(session)))
        {
            var pid=waiting.ProcessId!.Value;
            if(_readerWarmups.TryGetValue(waiting.Profile.Id,out var old))
            {
                if(old.ProcessId==pid)continue;
                File.WriteAllText(old.StopFile,"client changed");
            }
            var folder=Path.Combine(CycleQueue.Root,"reader-warmups");
            Directory.CreateDirectory(folder);
            var prefix=Path.Combine(folder,$"{pid}-{Guid.NewGuid():N}");
            var stop=prefix+".stop";
            try
            {
                var work=_collection.PrepareMarketReaderAsync(waiting,prefix+".json",stop);
                _readerWarmups[waiting.Profile.Id]=new(pid,stop,work);
                _=work.ContinueWith(done=>
                {
                    if(done.IsCompletedSuccessfully)Log($"INFO {root.Profile.Name}: PID {pid} · ридер лавок подготовлен до очереди");
                    else if(!done.IsCanceled && !File.Exists(stop))
                        Log($"WARNING {root.Profile.Name}: PID {pid} · ранняя подготовка ридера недоступна · {done.Exception?.GetBaseException().Message}");
                    _=done.Exception;
                });
                Log($"INFO {root.Profile.Name}: PID {pid} · ранняя подготовка ридера без игровых команд");
            }
            catch(Exception error){Log($"WARNING {root.Profile.Name}: PID {pid} · ранняя подготовка · {error.Message}");}
        }
    }

    private async Task StopMarketReaderWarmupsAsync(ProfileRuntime root)
    {
        _stoppedWarmupMarkets.Add(root.Profile.Id);
        var ids=MarketAccounts(root).Select(account=>account.Profile.Id).ToArray();
        var warmups=ids.Where(_readerWarmups.ContainsKey).Select(id=>_readerWarmups[id]).ToArray();
        foreach(var warmup in warmups.Where(item=>!item.Work.IsCompleted))
            File.WriteAllText(warmup.StopFile,"collection stopped");
        await Task.WhenAll(warmups.Select(async item=>{try{await item.Work;}catch{}}));
        foreach(var id in ids)_readerWarmups.Remove(id);
    }
}
