using PriceCheck.Collector.Models;
using PriceCheck.Launching;
using PriceCheck.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private readonly Dictionary<Guid,List<ProfileRuntime>> _accountRuntimes=[];
    private readonly Dictionary<Guid,ProfileRuntime> _marketActive=[];
    private readonly Dictionary<Guid,int> _lastBrokerOwner=[];

    public IReadOnlyList<AccountLaunchItem> AccountItems => SelectedRuntime is not { } root ? [] :
        MarketAccounts(root).Where(runtime=>!string.IsNullOrWhiteSpace(runtime.Profile.LoginName))
            .Select(runtime=>new AccountLaunchItem(runtime,
                LaunchTemplates.FirstOrDefault(template=>template.Id==runtime.Profile.LaunchTemplateId)?.Name ?? "Шаблон не выбран"))
            .ToArray();

    private void RefreshAccountItems() => OnPropertyChanged(nameof(AccountItems));

    private IReadOnlyList<ProfileRuntime> MarketAccounts(ProfileRuntime root) =>
        [root,.._accountRuntimes.GetValueOrDefault(root.Profile.Id,[])];

    private IEnumerable<ProfileRuntime> AllRuntimes() => Runtimes.SelectMany(MarketAccounts);

    private void RebuildAccountRuntimes(ProfileRuntime root)
    {
        if(MarketAccounts(root).Any(r=>r.Session is { } session && ClientProcessIdentity.IsCurrent(session)))
            throw new InvalidOperationException("Остановите клиентов перед изменением состава аккаунтов.");
        _accountRuntimes[root.Profile.Id]=root.Profile.RotationAccounts.Select(account=>
            new ProfileRuntime {Profile=CreateAccountProfile(root.Profile,account)}).ToList();
        root.AccountProcessCount=0;
        RefreshAccountItems();
    }

    private static CollectorProfile CreateAccountProfile(CollectorProfile market,RotationAccount account)
    {
        var profile=new CollectorProfile
        {
            Id=account.Id,Name=market.Name,City=market.City,Role=market.Role,
            LoginName=account.LoginName,LoginPassword=account.LoginPassword,
            LoginServerName=market.LoginServerName,LoginServerId=market.LoginServerId,
            LaunchTemplateId=account.LaunchTemplateId,CharacterSlot=account.CharacterSlot,
            CharacterRotationEnabled=account.CharacterRotationEnabled,
            RotationIntervalMinutes=account.RotationIntervalMinutes,
            RotationJitterMinutes=account.RotationJitterMinutes,
            AutoRestartEnabled=account.AutoRestartEnabled,AutoLoginEnabled=account.AutoLoginEnabled??market.AutoLoginEnabled
        };
        CopyMarketSettings(market,profile);
        return profile;
    }

    private static void CopyMarketSettings(CollectorProfile market,CollectorProfile account)
    {
        account.Name=market.Name;account.City=market.City;account.Role=market.Role;
        account.LoginServerName=market.LoginServerName;account.LoginServerId=market.LoginServerId;
        account.ClientFolder=market.ClientFolder;account.LaunchFile=market.LaunchFile;
        account.ServerUrl=market.ServerUrl;account.RecheckHours=market.RecheckHours;
        account.TraderPauseSeconds=market.TraderPauseSeconds;
        account.BrokerIntervalMinutes=market.BrokerIntervalMinutes;
        account.CenterZonesByCity=market.CenterZonesByCity.ToDictionary(pair=>pair.Key,
            pair=>new CenterZoneSettings{X=pair.Value.X,Y=pair.Value.Y});
        if(SavedGiranCenter.Resolve(market) is { } center)
            account.CenterZonesByCity["Giran"]=new(){X=center.X,Y=center.Y};
    }

    private void SyncAccountSettings()
    {
        foreach(var root in Runtimes)
        foreach(var child in _accountRuntimes.GetValueOrDefault(root.Profile.Id,[]))
        {
            CopyMarketSettings(root.Profile,child.Profile);
            var account=root.Profile.RotationAccounts.FirstOrDefault(a=>a.Id==child.Profile.Id);
            if(account is null)continue;
            account.CharacterSlot=child.Profile.CharacterSlot;
        }
    }

    private async Task LaunchChildAccountAsync(ProfileRuntime root,ProfileRuntime child)
    {
        if(child.IsBusy || _closing)return;
        if(child.Session is { } current && ClientProcessIdentity.IsCurrent(current))return;
        CopyMarketSettings(root.Profile,child.Profile);
        var template=LaunchTemplates.FirstOrDefault(t=>t.Id==child.Profile.LaunchTemplateId && t.Id!=Guid.Empty);
        root.IsBusy=child.IsBusy=true;
        try
        {
            if(template is null)throw new InvalidOperationException("Выберите шаблон запуска аккаунта.");
            CharacterRotationSchedule.Validate(child.Profile);
            child.Session=await _launcher.LaunchAsync(child.Profile,template,status=>child.LaunchStatus=status,
                CancellationToken.None,CaptureReaderBeforeLogin(child));
            await EnsureReaderAttachedAsync(child);
            _characterRotation.Started(child.Profile,child.Session,DateTimeOffset.UtcNow);
            child.Profile.LastProcessId=child.Session.ProcessId;
            child.Profile.LastProcessStartUtc=child.Session.StartedAtUtc;
            Log($"{root.Profile.Name}: аккаунт {child.Profile.LoginName} запущен · PID {child.ProcessId}");
            await SaveProfilesAsync();
        }
        catch(Exception e)
        {
            child.LaunchStatus=e.GetBaseException().Message;
            if(child.ReaderAttached)try {await _collection.DetachAsync(child);}catch(Exception cleanup)
            {Log($"WARNING {root.Profile.Name}: reader cleanup · {cleanup.Message}");}
            if(child.Session is not { } surviving || !ClientProcessIdentity.IsCurrent(surviving))child.Session=null;
            ShowModuleError(root,e);
        }
        finally
        {
            child.Protection=_launcher.Protection(child.Profile.Id);
            child.IsBusy=false;
            root.IsBusy=false;
            if(template?.RotateEachLaunch==true)await SaveTemplatesAsync();
            RefreshMarketDisplay(root);
        }
    }

    private async Task StopAdditionalAccountsAsync(ProfileRuntime root)
    {
        foreach(var child in _accountRuntimes.GetValueOrDefault(root.Profile.Id,[]))
        {
            _clientRecovery.Forget(child.Profile.Id);_characterRotation.Forget(child.Profile.Id);
            await _collection.DetachAsync(child);
            _launcher.Stop(child.Profile.Id);
            child.Session=null;child.Profile.LastProcessId=null;child.Profile.LastProcessStartUtc=null;
            child.LaunchStatus="Остановлен";
        }
    }

    private async Task AdvanceMarketTurnAsync(ProfileRuntime root)
    {
        if(!_marketActive.TryGetValue(root.Profile.Id,out var active))return;
        var accounts=MarketAccounts(root);
        if(active.Session is not { } current || !ClientProcessIdentity.IsCurrent(current))
        {
            var fallback=accounts.FirstOrDefault(candidate=>candidate!=active && candidate.ReaderAttached &&
                candidate.Session is { } live && ClientProcessIdentity.IsCurrent(live) && candidate.ClientFault is null &&
                candidate.Protection.GameConnected!=false);
            if(fallback is null)return;
            _clientRecovery.YieldCollection(active.Profile.Id);
            await _collection.DetachAsync(active);
            await _launcher.ValidateProtectionAsync(fallback.Profile.Id,true,CancellationToken.None);
            await _collection.SetCollectionAsync(fallback,true);
            _marketActive[root.Profile.Id]=fallback;
            _lastBrokerOwner[root.Profile.Id]=accounts.ToList().IndexOf(fallback);
            _characterRotation.Schedule.Pause(active.Profile.Id,DateTimeOffset.UtcNow);
            _characterRotation.Schedule.Resume(fallback.Profile.Id,DateTimeOffset.UtcNow);
            Log($"WARNING {root.Profile.Name}: аккаунт {active.Profile.LoginName} недоступен; ход передан {fallback.Profile.LoginName}");
            RefreshMarketDisplay(root);
            return;
        }
        var currentIndex=accounts.ToList().IndexOf(active);
        ProfileRuntime? NextReady(int start) => Enumerable.Range(1,accounts.Count-1)
            .Select(offset=>accounts[(start+offset)%accounts.Count])
            .FirstOrDefault(candidate=>candidate.ReaderAttached && candidate.Session is { } session &&
                ClientProcessIdentity.IsCurrent(session) && !candidate.IsBusy && candidate.ClientFault is null &&
                candidate.Protection.GameConnected!=false);
        if(!_collection.MarketTurnComplete(active))
        {
            PrepareWaitingAccountRoute(active);
            return;
        }
        var finishedPass=_collection.MarketPassComplete(active);
        var start=finishedPass?_lastBrokerOwner.GetValueOrDefault(root.Profile.Id,currentIndex):currentIndex;
        var next=NextReady(start);
        if(next is null)
        {
            _collection.ContinueMarketTurn(active);
            active.Status="Другие аккаунты недоступны; продолжает проверку";
            return;
        }
        if(!finishedPass && _readerWarmups.TryGetValue(next.Profile.Id,out var warmup) &&
           warmup.ProcessId==next.ProcessId && !warmup.Work.IsCompleted)
        {
            active.Status=$"Ожидает подготовку ридера PID {warmup.ProcessId} перед передачей хода";
            return;
        }
        await _collection.TransferMarketTurnAsync(active,next);
        _marketActive[root.Profile.Id]=next;
        _characterRotation.Schedule.Pause(active.Profile.Id,DateTimeOffset.UtcNow);
        _characterRotation.Schedule.Resume(next.Profile.Id,DateTimeOffset.UtcNow);
        if(finishedPass)_lastBrokerOwner[root.Profile.Id]=accounts.ToList().IndexOf(next);
        RefreshMarketDisplay(root);
    }

    private void PrepareWaitingAccountRoute(ProfileRuntime active,bool trace=false)
    {
        var root=Runtimes.FirstOrDefault(candidate=>MarketAccounts(candidate)
            .Any(account=>account.Profile.Id==active.Profile.Id));
        if(root is null)
        {
            if(trace)Log($"WARNING {active.Profile.Name}: предрасчёт · активный аккаунт не найден в профиле");
            return;
        }
        var accounts=MarketAccounts(root);
        var index=accounts.ToList().FindIndex(account=>account.Profile.Id==active.Profile.Id);
        if(index<0)return;
        var waiting=Enumerable.Range(1,accounts.Count-1)
            .Select(offset=>accounts[(index+offset)%accounts.Count])
            .FirstOrDefault(candidate=>candidate.ReaderAttached && candidate.Session is { } session &&
                ClientProcessIdentity.IsCurrent(session) && !candidate.IsBusy && candidate.ClientFault is null &&
                candidate.Protection.GameConnected!=false);
        if(waiting is null)
        {
            if(trace)Log($"INFO {root.Profile.Name}: предрасчёт ожидает свободный аккаунт после PID {active.ProcessId}");
            return;
        }
        try {_collection.PrepareMarketTurn(active,waiting);}
        catch(Exception error) {Log($"WARNING {root.Profile.Name}: предрасчёт маршрута · {error.Message}");}
    }

    private void RefreshMarketDisplay(ProfileRuntime root)
    {
        var accounts=MarketAccounts(root);
        root.AccountProcessCount=accounts.Count(r=>r.Session is { } session && ClientProcessIdentity.IsCurrent(session));
        if(accounts.Count>1)
            root.AccountsStatus=string.Join(Environment.NewLine,accounts.Select(account=>
            {
                var pid=account.Session is { } session && ClientProcessIdentity.IsCurrent(session)
                    ? $"PID {session.ProcessId}" : account.LaunchStatus;
                var turn=_marketActive.GetValueOrDefault(root.Profile.Id)==account
                    ? " · проверяет" : account.ReaderAttached ? " · ожидает" : "";
                return $"{account.Profile.LoginName}: {pid}{turn}";
            }));
        else root.AccountsStatus="";
        if(_marketActive.TryGetValue(root.Profile.Id,out var active) && active!=root)
        {
            root.Cycle=active.Cycle;
            root.Status=$"{active.Profile.LoginName}: {active.Status}";
            root.CharacterRotationStatus=$"{active.Profile.LoginName}: {active.CharacterRotationStatus}";
        }
        root.Cycle=_collection.MarketStatus(root.Profile.Name,root.Cycle);
        EnsureMarketReaderWarmups(root);
        root.MarketRadar=_collection.MarketRadar(root.Profile.Name);
        root.BrokerDelivery=accounts.Select(account=>account.BrokerDelivery)
            .Where(delivery=>delivery is not null).MaxBy(delivery=>delivery!.CapturedAtUtc);
        root.RefreshProfile();
    }
}
