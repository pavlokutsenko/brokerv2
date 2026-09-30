using System.Windows;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector;

public sealed record AccountSettings(string LoginName,string Password,Guid TemplateId,bool AutoLogin,
    int CharacterSlot,bool Rotate,int Interval,int Jitter,bool AutoRestart);

public partial class AccountEditorDialog : Window
{
    private readonly IReadOnlyList<string> _otherNames;
    private readonly IReadOnlySet<Guid> _otherTemplates;
    public AccountSettings? Settings { get; private set; }

    public AccountEditorDialog(Window owner,CollectorProfile? account,IReadOnlyList<LaunchTemplate> templates,
        IEnumerable<string> otherNames,IEnumerable<Guid>? otherTemplates=null)
    {
        InitializeComponent();Owner=owner;
        _otherNames=otherNames.ToArray();
        _otherTemplates=(otherTemplates??[]).ToHashSet();
        TemplateInput.ItemsSource=templates.Where(template=>template.Id!=Guid.Empty).ToArray();
        LoginInput.Text=account?.LoginName??"";
        PasswordInput.Text=account?.LoginPassword??"";
        TemplateInput.SelectedValue=account?.LaunchTemplateId;
        AutoLoginInput.IsChecked=account?.AutoLoginEnabled??false;
        CharacterSlotInput.Text=(account?.CharacterSlot??0).ToString();
        RotationInput.IsChecked=account?.CharacterRotationEnabled??false;
        SyncRotationAvailability();
        IntervalInput.Text=(account?.RotationIntervalMinutes??60).ToString();
        JitterInput.Text=(account?.RotationJitterMinutes??15).ToString();
        AutoRestartInput.IsChecked=account?.AutoRestartEnabled??false;
        Loaded+=(_,_)=>LoginInput.Focus();
    }

    private void Cancel_Click(object sender,RoutedEventArgs e)=>DialogResult=false;
    private void AutoLogin_Changed(object sender,RoutedEventArgs e)=>SyncRotationAvailability();
    private void SyncRotationAvailability()
    {
        if(RotationInput is null)return;
        var enabled=AutoLoginInput.IsChecked==true;
        if(!enabled)RotationInput.IsChecked=false;
        RotationInput.IsEnabled=enabled;
    }
    private void Save_Click(object sender,RoutedEventArgs e)
    {
        var login=LoginInput.Text.Trim();var password=PasswordInput.Text;
        var autoLogin=AutoLoginInput.IsChecked==true;
        var rotate=RotationInput.IsChecked==true;
        if(login.Length is <1 or >120 || login.Contains('\0'))
        {Invalid("Укажите логин до 120 символов.",LoginInput);return;}
        if(_otherNames.Contains(login,StringComparer.OrdinalIgnoreCase))
        {Invalid("Такой логин уже есть в этом профиле.",LoginInput);return;}
        if(password.Length>120 || password.Contains('\0') || login.Length+password.Length+2>256)
        {Invalid("Пароль слишком длинный или содержит недопустимый символ.",PasswordInput);return;}
        if(TemplateInput.SelectedValue is not Guid templateId || templateId==Guid.Empty)
        {Invalid("Выберите шаблон запуска.",TemplateInput);return;}
        if(_otherTemplates.Contains(templateId))
        {Invalid("Этот шаблон уже назначен другому аккаунту профиля.",TemplateInput);return;}
        if(autoLogin && password.Length==0)
        {Invalid("Для автовхода укажите пароль.",PasswordInput);return;}
        if(rotate && !autoLogin)
        {Invalid("Для ротации персонажей включите автовход.",AutoLoginInput);return;}
        if(!int.TryParse(CharacterSlotInput.Text,out var slot) || slot is <0 or >6)
        {Invalid("Слот персонажа должен быть от 0 до 6.",CharacterSlotInput);return;}
        if(!int.TryParse(IntervalInput.Text,out var interval) || interval is <1 or >1440)
        {Invalid("Интервал ротации должен быть от 1 до 1440 минут.",IntervalInput);return;}
        if(!int.TryParse(JitterInput.Text,out var jitter) || jitter<0 || jitter>=interval)
        {Invalid("Разброс должен быть меньше интервала и не может быть отрицательным.",JitterInput);return;}
        Settings=new(login,password,templateId,autoLogin,slot,rotate,interval,jitter,
            AutoRestartInput.IsChecked==true);
        DialogResult=true;
    }

    private void Invalid(string message,FrameworkElement field)
    {
        MessageBox.Show(this,message,"PriceCheck Collector",MessageBoxButton.OK,MessageBoxImage.Warning);
        field.Focus();
    }
}
