using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PriceCheck.Collector;
using PriceCheck.Collector.Models;

internal static class Program
{
    [STAThread] private static void Main(string[] args)
    {
        if(args is ["--accounts-only",var accountsOutput])
        {
            var accountsApp=new App(enableRuntime:false);accountsApp.InitializeComponent();
            var accountsWindow=new MainWindow(false){ShowInTaskbar=false,Left=-20000,Top=-20000};
            accountsWindow.Show();
            CheckAccountEditor(accountsWindow,accountsOutput);
            accountsWindow.Close();accountsApp.Shutdown();
            Console.WriteLine("ROTATION_ACCOUNTS_UI_OK add masked_list layout");
            return;
        }
        var app=new App(enableRuntime:false);app.InitializeComponent();
        var runtime=new ProfileRuntime{Profile=new(){AutoLoginEnabled=true,LoginName="primary",LoginPassword="test-secret",RotationCharacterCount=7},
            CharacterRotationStatus="Character 0 of 7 · next change 17:30:00 · 60.0 min",LaunchStatus="Client ready"};
        var window=new MainWindow(false){ShowInTaskbar=false,Left=-20000,Top=-20000};
        window.Runtimes.Add(runtime);window.SelectedRuntime=runtime;
        window.Show();app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var view=(LaunchPanelView)window.FindName("LaunchPanel")!;
        runtime.Protection=new(true,true,true,2,69,13,"FIXTURE123",null);
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(!((TextBlock)view.FindName("HardwareProtectionIndicator")!).Text.Contains("FIXTURE123") ||
            !((TextBlock)view.FindName("ProxyProtectionIndicator")!).Text.Contains("2 CONNECT"))
            throw new Exception("Collector protection indicators did not bind to the selected runtime.");
        runtime.Protection = runtime.Protection with { ProxyReady = false, ProxyRequired = false };
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var proxyIndicator = (TextBlock)view.FindName("ProxyProtectionIndicator")!;
        if (!proxyIndicator.Text.Contains("выключен в шаблоне") ||
            ((SolidColorBrush)proxyIndicator.Foreground).Color != Color.FromRgb(0x8F,0x9C,0xAB))
            throw new Exception("Disabled proxy was displayed as an error or active protection.");
        var toggle=(CheckBox)view.FindName("RotationToggle")!;
        var interval=(TextBox)view.FindName("RotationIntervalInput")!;
        var jitter=(TextBox)view.FindName("RotationJitterInput")!;
        var restart=(CheckBox)view.FindName("AutoRestartToggle")!;
        if(restart.IsChecked!=true) throw new Exception("Client recovery is not enabled by default.");
        restart.IsChecked=false;
        if(runtime.Profile.AutoRestartEnabled) throw new Exception("Recovery toggle did not update the profile.");
        restart.IsChecked=true;
        if(interval.Text!="60" || jitter.Text!="15" || toggle.IsChecked!=false) throw new Exception("Rotation defaults not visible.");
        toggle.IsChecked=true;
        interval.Text="90";interval.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        jitter.Text="10";jitter.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        if(!runtime.Profile.CharacterRotationEnabled || runtime.Profile.RotationIntervalMinutes!=90 || runtime.Profile.RotationJitterMinutes!=10)
            throw new Exception("Rotation controls do not update the selected profile.");
        interval.Text="60";jitter.Text="15";
        CheckAccountEditor(window,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!,"rotation-accounts.png"));
        runtime.CharacterRotationStatus="Character 0 of 7 · next change 17:30:00 · 60.0 min";
        var collection=(CollectionPanelView)window.FindName("CollectionPanel")!;
        if(((TextBlock)collection.FindName("CharacterRotationCountdown")!).Text!=runtime.CharacterRotationStatus)
            throw new Exception("Collection panel does not show the actual rotation clock.");
        window.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
        using(var output=File.Create(args[0])) encoder.Save(output);
        var closing=typeof(MainWindow).GetMethod("MainWindow_Closing",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        window.Closing+=(sender,e)=>closing.Invoke(window,[sender,e]);
        window.Close();app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(window.IsVisible) throw new Exception("Idle collector close was canceled or remained stuck.");
        app.Shutdown();Console.WriteLine("CHARACTER_ROTATION_UI_OK defaults editable_fields toggle recovery idle_close");
    }

    private static void CheckAccountEditor(MainWindow window,string output)
    {
        var previous=window.SelectedRuntime;
        var profile=new CollectorProfile{LoginName="primary",LoginPassword="primary-secret"};
        var runtime=new ProfileRuntime{Profile=profile};
        window.Runtimes.Add(runtime);window.SelectedRuntime=runtime;
        window.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var view=(LaunchPanelView)window.FindName("LaunchPanel")!;
        if(!((Button)view.FindName("RotationAccountsButton")!).IsEnabled)
            throw new Exception("Account editor unavailable for a stopped profile.");
        var accounts=new RotationAccountsDialog(window,profile){ShowInTaskbar=false};
        accounts.Show();window.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var name=(TextBox)accounts.FindName("LoginInput")!;
        var password=(PasswordBox)accounts.FindName("PasswordInput")!;
        name.Text="secondary";password.Password="secondary-secret";
        ((Button)accounts.FindName("SaveAccountButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(accounts.Accounts.Count!=1 || accounts.Accounts[0].LoginName!="secondary" ||
           accounts.Accounts[0].LoginPassword!="secondary-secret")
            throw new Exception("Rotation account editor did not add an account draft.");
        accounts.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)accounts.ActualWidth,(int)accounts.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(accounts);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using(var stream=File.Create(output))encoder.Save(stream);
        accounts.Close();
        window.SelectedRuntime=previous;
    }
}
