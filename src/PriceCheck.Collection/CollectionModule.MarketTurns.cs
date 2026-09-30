using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Windows.Storage;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private sealed record MarketTurnPlan(Guid AccountId,int ProcessId,string TargetKey,double X,double Y,Task Work);

    public void PrepareMarketTurn(ProfileRuntime active,ProfileRuntime next)
    {
        if(!_cycles.TryGetValue(active.Profile.Id,out var cycle) || cycle.ActiveTraderKey is not { } activeKey)return;
        var reason=!active.OneTraderPerTurn ? "ходы аккаунтов выключены" :
            !active.IsCollectionEnabled || cycle.Phase!="Reading prices" ? "активный ход не готов" :
            !cycle.Store.HasPendingPass ? "общая очередь пуста" :
            !_marketOwners.TryGetValue(CycleQueue.Key(active.Profile.Name),out var owner) || owner!=active.Profile.Id
                ? "нет владельца рынка" :
            next.IsCollectionEnabled ? "следующий клиент уже собирает" :
            !_attached.TryGetValue(next.Profile.Id,out var attached) || !_isCurrent(attached)
                ? "ожидающий ридер не подключён" :
            next.Radar is not {LivePlayerPositionAvailable:true} ? "позиция ожидающего клиента недоступна" :
            GetCenterZone(next.Profile)!=cycle.Center ? "центры аккаунтов различаются" : null;
        if(reason is not null)
        {
            var diagnostic=$"{activeKey}:{next.Profile.Id}:{reason}";
            if(cycle.PlanWaitReason!=diagnostic)
            {
                cycle.PlanWaitReason=diagnostic;
                Log($"INFO {active.Profile.Name}: предрасчёт PID {next.ProcessId} ожидает · {reason}");
            }
            return;
        }
        var session=_attached[next.Profile.Id];
        var radar=next.Radar!;
        var target=cycle.Store.PreviewNextTarget(radar.PlayerX,radar.PlayerY,cycle.RadarPool,activeKey);
        if(target is null)
        {
            var diagnostic=$"{activeKey}:{next.Profile.Id}:нет следующей цели";
            if(cycle.PlanWaitReason!=diagnostic)
            {
                cycle.PlanWaitReason=diagnostic;
                Log($"INFO {active.Profile.Name}: предрасчёт PID {next.ProcessId} ожидает · нет следующей цели");
            }
            return;
        }
        var prior=cycle.UpcomingPlan;
        if(prior is not null)
        {
            if(!prior.Work.IsCompleted)return;
            if(prior.Work.IsCompletedSuccessfully && prior.AccountId==next.Profile.Id && prior.ProcessId==session.ProcessId &&
               prior.TargetKey==target.TraderKey && Math.Abs(prior.X-radar.PlayerX)<5 &&
               Math.Abs(prior.Y-radar.PlayerY)<5)return;
        }
        var name=next.Profile.Id.ToString("N");
        var input=Path.Combine(cycle.Folder,$"next-section-{name}.input.json");
        var output=Path.Combine(cycle.Folder,$"next-section-{name}.plan.json");
        DurableJsonFile.Write(input,new {city="Giran",planningStart=new[]{radar.PlayerX,radar.PlayerY},
            targets=RouteTargets([target])},keepBackup:false);
        var work=Task.Run(()=>_worker.RunAsync(session,"market-plan",output,start=>{
            start.ArgumentList.Add("--input");start.ArgumentList.Add(input);
        }));
        cycle.UpcomingPlan=new(next.Profile.Id,session.ProcessId,target.TraderKey,radar.PlayerX,radar.PlayerY,work);
        _=work.ContinueWith(done=>{
            if(done.Exception is {} error)
                Log($"WARNING {active.Profile.Name}: предрасчёт PID {session.ProcessId} · {error.GetBaseException().Message}");
            else if(done.IsCanceled)
                Log($"WARNING {active.Profile.Name}: предрасчёт PID {session.ProcessId} отменён · будет повторён");
            else if(done.IsCompletedSuccessfully)
                Log($"INFO {active.Profile.Name}: маршрут для ожидающего PID {session.ProcessId} готов · {target.Name}");
        });
        Log($"INFO {active.Profile.Name}: ожидающий PID {session.ProcessId} рассчитывает маршрут к {target.Name} · без игровых команд");
    }

    public bool MarketTurnComplete(ProfileRuntime runtime) =>
        _cycles.TryGetValue(runtime.Profile.Id,out var cycle) && cycle.TraderTurnComplete &&
        cycle.RouteSession is null &&
        (!_activeJobs.TryGetValue(runtime.Profile.Id,out var job) || job.IsCompleted);

    public bool MarketPassComplete(ProfileRuntime runtime) =>
        MarketTurnComplete(runtime) && (_cycles[runtime.Profile.Id].Phase=="Return to center" ||
            !_cycles[runtime.Profile.Id].Store.HasPendingPass);

    public void ContinueMarketTurn(ProfileRuntime runtime)
    {
        if(!_cycles.TryGetValue(runtime.Profile.Id,out var cycle))return;
        if(!cycle.Store.HasPendingPass)cycle.Phase="Return to center";
        cycle.TraderTurnComplete=false;
        cycle.Next=DateTimeOffset.MinValue;
    }

    public async Task TransferMarketTurnAsync(ProfileRuntime from,ProfileRuntime to)
    {
        var source=from.Profile.Id;var destination=to.Profile.Id;
        if(source==destination)throw new InvalidOperationException("The next market account must be different.");
        if(!_transitions.Add(source))throw new InvalidOperationException("Source reader is changing.");
        if(!_transitions.Add(destination))
        { _transitions.Remove(source);throw new InvalidOperationException("Destination reader is changing."); }
        try
        {
            if(CycleQueue.Key(from.Profile.Name)!=CycleQueue.Key(to.Profile.Name) ||
               from.Profile.City!=to.Profile.City || from.Profile.ServerUrl!=to.Profile.ServerUrl)
                throw new InvalidOperationException("Market accounts must share their market settings.");
            if(!_marketOwners.TryGetValue(CycleQueue.Key(from.Profile.Name),out var owner) || owner!=source ||
               !from.IsCollectionEnabled || !_cycles.TryGetValue(source,out var cycle) || !cycle.TraderTurnComplete)
                throw new InvalidOperationException("The source account has no completed market turn.");
            if(!_attached.TryGetValue(destination,out var session) || !_isCurrent(session) || to.IsCollectionEnabled)
                throw new InvalidOperationException("The next account has no ready reader.");
            if(GetCenterZone(to.Profile)!=cycle.Center ||
               !Uri.TryCreate(to.Profile.ServerUrl,UriKind.Absolute,out var server) ||
               server.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("The next account has invalid market settings.");
            if(_activeJobs.TryGetValue(source,out var job))await job.WaitAsync(TimeSpan.FromMinutes(2));
            await CleanupPriceSessionAsync(from);
            var continuePass=cycle.Phase=="Reading prices" && cycle.Store.HasPendingPass;
            from.IsCollectionEnabled=from.Profile.CollectionEnabled=false;
            from.Status="Ожидает очередь проверки";
            RequestCycleStop(from);
            StopBrokerSchedule(from);
            _marketOwners.Remove(CycleQueue.Key(from.Profile.Name));
            _activeJobs.Remove(source);
            _cycles.Remove(source);
            if(continuePass)
            {
                cycle.Bindings.BeginSession();cycle.RadarPool.BeginSession();
                cycle.ContinueAfterClientChange=true;
                cycle.TraderTurnComplete=false;cycle.ActiveTraderKey=null;
                cycle.Phase="Resume route";cycle.Next=DateTimeOffset.MinValue;
                cycle.PreviousDestination=null;cycle.BackgroundPlan=null;
                cycle.PlanGeneration=destination.ToString("N");cycle.UpcomingPlan=null;
                cycle.PriceStreamPrefix=null;cycle.PriceEventReader=null;cycle.PriceSpooled.Clear();
                _cycles[destination]=cycle;
            }
            _localStoreSessions.Remove(destination);
            try {await SetCollectionCoreAsync(to,true);}
            catch
            {
                _cycles.Remove(destination);
                try
                {
                    if(continuePass)
                    {
                        cycle.ContinueAfterClientChange=false;
                        cycle.TraderTurnComplete=false;
                        cycle.Phase="Reading prices";
                        _cycles[source]=cycle;
                        File.Delete(cycle.StopFile);
                    }
                    else StartCycle(from);
                    from.IsCollectionEnabled=from.Profile.CollectionEnabled=true;
                    _marketOwners[CycleQueue.Key(from.Profile.Name)]=source;
                    _nextBrokerRuns[source]=DateTimeOffset.MinValue;
                }
                catch
                {
                    from.IsCollectionEnabled=from.Profile.CollectionEnabled=false;
                    _marketOwners.Remove(CycleQueue.Key(from.Profile.Name));
                }
                throw;
            }
            to.Status=continuePass?"Продолжает общий пул проверок":"Следующий брокерный проход";
            Log($"INFO {from.Profile.Name}: очередь PID {from.ProcessId} → PID {to.ProcessId} · {(continuePass?"общий пул сохранён":"новый брокерный цикл")}");
        }
        finally
        { _transitions.Remove(destination);_transitions.Remove(source); }
    }
}
