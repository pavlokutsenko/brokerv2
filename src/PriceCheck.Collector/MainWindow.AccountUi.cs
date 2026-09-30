using System.Windows;
using System.Windows.Controls;
using PriceCheck.Collector.Models;
using PriceCheck.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private ProfileRuntime? AccountFromButton(object sender) =>
        sender is Button {Tag:ProfileRuntime account} && SelectedRuntime is { } root &&
        MarketAccounts(root).Contains(account) ? account : null;

    private async void AddAccount_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedRuntime is not {CanEditMarket:true} root)return;
        await EditAccountAsync(root,null);
    }

    private async void EditAccount_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedRuntime is not {CanEditMarket:true} root || AccountFromButton(sender) is not { } account)return;
        await EditAccountAsync(root,account);
    }

    private async Task EditAccountAsync(ProfileRuntime root,ProfileRuntime? selected)
    {
        try
        {
        var accounts=MarketAccounts(root);
        if(selected is null && accounts.Count>20)
        {MessageBox.Show(this,"Достигнут предел аккаунтов этого сервера.");return;}
        var target=selected ?? (string.IsNullOrWhiteSpace(root.Profile.LoginName)?root:null);
        var dialog=new AccountEditorDialog(this,target?.Profile,LaunchTemplates,
            accounts.Where(account=>account!=target).Select(account=>account.Profile.LoginName),
            accounts.Where(account=>account!=target).Select(account=>account.Profile.LaunchTemplateId));
        if(dialog.ShowDialog()!=true || dialog.Settings is not { } settings)return;
        if(target==root)
        {
            ApplyAccountSettings(root.Profile,settings);
        }
        else if(target is not null)
        {
            var saved=root.Profile.RotationAccounts.Single(account=>account.Id==target.Profile.Id);
            ApplyAccountSettings(target.Profile,settings);
            CopyAccountSettings(target.Profile,saved);
        }
        else
        {
            var profile=new CollectorProfile();
            ApplyAccountSettings(profile,settings);
            var added=new RotationAccount{Id=profile.Id};
            CopyAccountSettings(profile,added);
            root.Profile.RotationAccounts.Add(added);
        }
        RebuildAccountRuntimes(root);
        root.RefreshProfile();
        await SaveProfilesAsync();
        }
        catch(Exception exception){ShowModuleError(root,exception);}
    }

    private static void ApplyAccountSettings(CollectorProfile profile,AccountSettings settings)
    {
        if(!profile.LoginName.Equals(settings.LoginName,StringComparison.OrdinalIgnoreCase))
        {profile.RotationCharacterCount=0;profile.RotationCharacterSlots=[];}
        profile.LoginName=settings.LoginName;
        profile.LoginPassword=settings.Password;
        profile.LaunchTemplateId=settings.TemplateId;
        profile.AutoLoginEnabled=settings.AutoLogin;
        profile.CharacterSlot=settings.CharacterSlot;
        profile.CharacterRotationEnabled=settings.Rotate;
        profile.RotationIntervalMinutes=settings.Interval;
        profile.RotationJitterMinutes=settings.Jitter;
        profile.AutoRestartEnabled=settings.AutoRestart;
    }

    private static void CopyAccountSettings(CollectorProfile profile,RotationAccount saved)
    {
        saved.LoginName=profile.LoginName;saved.LoginPassword=profile.LoginPassword;
        saved.LaunchTemplateId=profile.LaunchTemplateId;saved.AutoLoginEnabled=profile.AutoLoginEnabled;
        saved.CharacterSlot=profile.CharacterSlot;saved.CharacterRotationEnabled=profile.CharacterRotationEnabled;
        saved.RotationIntervalMinutes=profile.RotationIntervalMinutes;
        saved.RotationJitterMinutes=profile.RotationJitterMinutes;
        saved.AutoRestartEnabled=profile.AutoRestartEnabled;
    }

    private async void DeleteAccount_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedRuntime is not {CanEditMarket:true} root || AccountFromButton(sender) is not { } target)return;
        if(MessageBox.Show(this,$"Удалить аккаунт «{target.Profile.LoginName}»?","PriceCheck Collector",
            MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        try
        {
        if(target==root)
        {
            var replacement=root.Profile.RotationAccounts.FirstOrDefault();
            if(replacement is null)
            {
                root.Profile.LoginName="";root.Profile.LoginPassword="";root.Profile.LoginPasswordProtected=null;
                root.Profile.LaunchTemplateId=Guid.Empty;root.Profile.AutoLoginEnabled=false;
                root.Profile.CharacterRotationEnabled=false;
            }
            else
            {
                var values=CreateAccountProfile(root.Profile,replacement);
                ApplyAccountSettings(root.Profile,new(values.LoginName,values.LoginPassword,values.LaunchTemplateId,
                    values.AutoLoginEnabled,values.CharacterSlot,values.CharacterRotationEnabled,
                    values.RotationIntervalMinutes,values.RotationJitterMinutes,values.AutoRestartEnabled));
                root.Profile.RotationAccounts.Remove(replacement);
            }
        }
        else root.Profile.RotationAccounts.RemoveAll(account=>account.Id==target.Profile.Id);
        RebuildAccountRuntimes(root);
        await SaveProfilesAsync();
        }
        catch(Exception exception){ShowModuleError(root,exception);}
    }

    private async void LaunchAccount_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedRuntime is not { } root || AccountFromButton(sender) is not { } account)return;
        if(_marketActive.ContainsKey(root.Profile.Id))
        {
            MessageBox.Show(this,"Сначала остановите сбор этого сервера, затем запустите аккаунт.",
                "PriceCheck Collector",MessageBoxButton.OK,MessageBoxImage.Information);
            return;
        }
        if(account==root)await LaunchProfileAsync(root);
        else await LaunchChildAccountAsync(root,account);
    }

    private async void StopAccount_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedRuntime is not { } root || AccountFromButton(sender) is not {IsBusy:false} account)return;
        if(account!=root)root.IsBusy=true;
        account.IsBusy=true;
        try
        {
            if(_marketActive.GetValueOrDefault(root.Profile.Id)==account)
            {
                await _collection.SetCollectionAsync(account,false);
                _marketActive.Remove(root.Profile.Id);
                root.MarketCollectionEnabled=false;
                foreach(var waiting in MarketAccounts(root))
                    _characterRotation.Schedule.Resume(waiting.Profile.Id,DateTimeOffset.UtcNow);
            }
            _clientRecovery.Forget(account.Profile.Id);_characterRotation.Forget(account.Profile.Id);
            await _collection.DetachAsync(account);
            _launcher.Stop(account.Profile.Id);
            account.Session=null;account.Profile.LastProcessId=null;account.Profile.LastProcessStartUtc=null;
            account.LaunchStatus="Остановлен";
            RefreshMarketDisplay(root);
            await SaveProfilesAsync();
        }
        catch(Exception exception){ShowModuleError(root,exception);}
        finally{account.IsBusy=false;if(account!=root)root.IsBusy=false;}
    }

    private async void AccountLaunchFile_Changed(object sender,TextChangedEventArgs e)
    {
        if(!_loaded || _closing || sender is not FrameworkElement {DataContext:MainWindow} || SelectedRuntime is null)return;
        await SaveProfilesAsync();
    }
}
