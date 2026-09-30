using PriceCheck.Collector;
using PriceCheck.Collector.Models;
using PriceCheck.Windows;

namespace PriceCheck.Launcher;

public partial class MainWindow
{
    private readonly Dictionary<Guid,List<LaunchRuntime>> _accountRuntimes=[];
    public IReadOnlyList<AccountLaunchItem> AccountItems => SelectedRuntime is not { } root ? [] :
        MarketAccounts(root).Where(runtime=>!string.IsNullOrWhiteSpace(runtime.Profile.LoginName))
            .Select(runtime=>new AccountLaunchItem(runtime,
                LaunchTemplates.FirstOrDefault(template=>template.Id==runtime.Profile.LaunchTemplateId)?.Name ?? "Шаблон не выбран"))
            .ToArray();

    private IReadOnlyList<LaunchRuntime> MarketAccounts(LaunchRuntime root) =>
        [root,.._accountRuntimes.GetValueOrDefault(root.Profile.Id,[])];
    private IEnumerable<LaunchRuntime> AllAccounts() => Runtimes.SelectMany(MarketAccounts);
    private LaunchRuntime RootOf(LaunchRuntime runtime) =>
        Runtimes.FirstOrDefault(root=>MarketAccounts(root).Contains(runtime))??runtime;

    private void RebuildAccountRuntimes(LaunchRuntime root)
    {
        if(MarketAccounts(root).Any(runtime=>runtime.Session is { } session && ClientProcessIdentity.IsCurrent(session)))
            throw new InvalidOperationException("Остановите клиенты перед изменением аккаунтов.");
        _accountRuntimes[root.Profile.Id]=root.Profile.RotationAccounts.Select(account=>new LaunchRuntime
        {Profile=CreateAccountProfile(root.Profile,account)}).ToList();
        root.AccountProcessCount=0;
        Changed(nameof(AccountItems));
    }

    private static CollectorProfile CreateAccountProfile(CollectorProfile market,RotationAccount account)
    {
        var profile=new CollectorProfile
        {
            Id=account.Id,Name=market.Name,City=market.City,Role=market.Role,
            LoginName=account.LoginName,LoginPassword=account.LoginPassword,
            LaunchTemplateId=account.LaunchTemplateId,CharacterSlot=account.CharacterSlot,
            AutoLoginEnabled=account.AutoLoginEnabled??market.AutoLoginEnabled,
            CharacterRotationEnabled=account.CharacterRotationEnabled,
            RotationIntervalMinutes=account.RotationIntervalMinutes,
            RotationJitterMinutes=account.RotationJitterMinutes,
            AutoRestartEnabled=account.AutoRestartEnabled
        };
        CopyMarketSettings(market,profile);
        return profile;
    }

    private static void CopyMarketSettings(CollectorProfile market,CollectorProfile account)
    {
        account.Name=market.Name;account.City=market.City;
        account.LoginServerName=market.LoginServerName;account.LoginServerId=market.LoginServerId;
        account.ClientFolder=market.ClientFolder;account.LaunchFile=market.LaunchFile;
    }

    private void SyncAccountSettings()
    {
        foreach(var root in Runtimes)
        foreach(var child in _accountRuntimes.GetValueOrDefault(root.Profile.Id,[]))
        {
            CopyMarketSettings(root.Profile,child.Profile);
            var saved=root.Profile.RotationAccounts.FirstOrDefault(account=>account.Id==child.Profile.Id);
            if(saved is not null)saved.CharacterSlot=child.Profile.CharacterSlot;
        }
    }

    private void RefreshAccountCounts()
    {
        foreach(var root in Runtimes)
            root.AccountProcessCount=MarketAccounts(root).Count(runtime=>runtime.Session is { } session && ClientProcessIdentity.IsCurrent(session));
    }
}
