using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector;

public partial class RotationAccountsDialog : Window
{
    private readonly string _primaryName;
    private readonly ObservableCollection<RotationAccount> _accounts;
    public IReadOnlyList<RotationAccount> Accounts => _accounts.ToArray();

    public RotationAccountsDialog(Window owner,CollectorProfile profile)
    {
        InitializeComponent();
        Owner=owner;
        _primaryName=profile.LoginName.Trim();
        PrimaryName.Text=_primaryName.Length==0?"Не задан":_primaryName;
        _accounts=new(profile.RotationAccounts.Select(account=>new RotationAccount
        {
            Id=account.Id,LoginName=account.LoginName,LoginPassword=account.LoginPassword,
            LoginPasswordProtected=account.LoginPasswordProtected
        }));
        AccountsList.ItemsSource=_accounts;
        Loaded+=(_,_)=>LoginInput.Focus();
    }

    private void AccountsList_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        var account=AccountsList.SelectedItem as RotationAccount;
        LoginInput.Text=account?.LoginName??"";
        PasswordInput.Clear();
        SaveAccountButton.Content=account is null?"Добавить аккаунт":"Сохранить аккаунт";
    }

    private void New_Click(object sender,RoutedEventArgs e)
    {
        AccountsList.SelectedItem=null;
        LoginInput.Clear();PasswordInput.Clear();LoginInput.Focus();
    }

    private void Delete_Click(object sender,RoutedEventArgs e)
    {
        if(AccountsList.SelectedItem is RotationAccount account)_accounts.Remove(account);
    }

    private void MoveUp_Click(object sender,RoutedEventArgs e)
    {
        if(AccountsList.SelectedIndex is var index && index>0)_accounts.Move(index,index-1);
    }

    private void MoveDown_Click(object sender,RoutedEventArgs e)
    {
        if(AccountsList.SelectedIndex is var index && index>=0 && index<_accounts.Count-1)
            _accounts.Move(index,index+1);
    }

    private void SaveAccount_Click(object sender,RoutedEventArgs e)=>SaveAccount();

    private bool SaveAccount()
    {
        var name=LoginInput.Text.Trim();var password=PasswordInput.Password;
        var selected=AccountsList.SelectedItem as RotationAccount;
        if(name.Length==0 || name.Length>120 || name.Contains('\0') ||
           _accounts.Any(account=>account!=selected && account.LoginName.Equals(name,StringComparison.OrdinalIgnoreCase)) ||
           name.Equals(_primaryName,StringComparison.OrdinalIgnoreCase) ||
           password.Length>120 || password.Contains('\0') || name.Length+password.Length+2>256 ||
           selected is null && password.Length==0 || selected is null && _accounts.Count>=20)
        {
            MessageBox.Show(this,"Проверьте логин и пароль: аккаунт должен быть уникальным, а длина каждого поля не больше 120 символов.",
                "PriceCheck Collector",MessageBoxButton.OK,MessageBoxImage.Warning);
            return false;
        }
        if(selected is null)
        {
            selected=new RotationAccount{LoginName=name,LoginPassword=password};
            _accounts.Add(selected);
        }
        else
        {
            selected.LoginName=name;
            if(password.Length>0)selected.LoginPassword=password;
            AccountsList.Items.Refresh();
        }
        AccountsList.SelectedItem=selected;
        PasswordInput.Clear();
        return true;
    }

    private void Cancel_Click(object sender,RoutedEventArgs e)=>DialogResult=false;
    private void Done_Click(object sender,RoutedEventArgs e)
    {
        if(_accounts.Count>0 && _primaryName.Length==0)
        {
            MessageBox.Show(this,"Сначала задайте логин основного аккаунта на вкладке запуска.",
                "PriceCheck Collector",MessageBoxButton.OK,MessageBoxImage.Warning);
            return;
        }
        var selected=AccountsList.SelectedItem as RotationAccount;
        if(selected is not null && (LoginInput.Text.Trim()!=selected.LoginName || PasswordInput.Password.Length>0) ||
           selected is null && (LoginInput.Text.Trim().Length>0 || PasswordInput.Password.Length>0))
            if(!SaveAccount())return;
        DialogResult=true;
    }
}
