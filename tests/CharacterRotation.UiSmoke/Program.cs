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
        var output=Path.GetFullPath(args[^1]);
        var app=new App(enableRuntime:false);app.InitializeComponent();
        var template=new LaunchTemplate{Name="Account HWID"};
        var profile=new CollectorProfile{LoginName="first",LoginPassword="secret",
            LaunchTemplateId=template.Id,AutoLoginEnabled=true};
        var runtime=new ProfileRuntime{Profile=profile};
        var window=new MainWindow(false){ShowInTaskbar=false,Left=-20000,Top=-20000};
        window.LaunchTemplates.Add(template);window.Runtimes.Add(runtime);window.SelectedRuntime=runtime;
        window.Show();Pump(app);
        var panel=(AccountLaunchPanelView)window.FindName("LaunchPanel")!;
        Check(window.AccountItems.Count==1 && window.AccountItems[0].TemplateName=="Account HWID",
            "Account list did not show its launch template.");
        var hardware=FindText(panel,"HWID ·");
        var proxy=FindText(panel,"ПРОКСИ ·");
        runtime.Protection=new(true,true,true,2,69,13,"FIXTURE123",null);Pump(app);
        Check(hardware.Text.Contains("FIXTURE123") && proxy.Text.Contains("2 CONNECT"),
            $"Per-account protection did not bind (HWID={hardware.Text}; proxy={proxy.Text}).");
        runtime.Protection=runtime.Protection with{ProxyReady=false,ProxyRequired=false};Pump(app);
        Check(proxy.Text.Contains("отключён в шаблоне"),"Disabled proxy was displayed as active.");

        var second=new LaunchTemplate{Name="Second HWID"};
        var dialog=new AccountEditorDialog(window,null,[template,second],["first"],[template.Id])
            {ShowInTaskbar=false};
        dialog.Dispatcher.BeginInvoke(new Action(()=>
        {
            ((TextBox)dialog.FindName("LoginInput")!).Text="second";
            ((ComboBox)dialog.FindName("TemplateInput")!).SelectedValue=second.Id;
            FindButton(dialog,"Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }),DispatcherPriority.ApplicationIdle);
        dialog.ShowDialog();
        Check(dialog.Settings is {LoginName:"second",AutoLogin:false,Password:""} && dialog.DialogResult==true,
            "Manual account setup did not allow an empty saved password.");

        var rotation=new AccountEditorDialog(window,profile,[template],[],[]){ShowInTaskbar=false};
        rotation.Dispatcher.BeginInvoke(new Action(()=>
        {
            Check(((TextBox)rotation.FindName("PasswordInput")!).Text=="secret",
                "Saved password is not visible in the account editor.");
            var autoLogin=(CheckBox)rotation.FindName("AutoLoginInput")!;
            autoLogin.IsChecked=false;
            var rotate=(CheckBox)rotation.FindName("RotationInput")!;
            Check(rotate.IsChecked==false && !rotate.IsEnabled,
                "Manual login did not release character rotation.");
            autoLogin.IsChecked=true;
            ((CheckBox)rotation.FindName("RotationInput")!).IsChecked=true;
            ((TextBox)rotation.FindName("IntervalInput")!).Text="90";
            ((TextBox)rotation.FindName("JitterInput")!).Text="10";
            FindButton(rotation,"Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }),DispatcherPriority.ApplicationIdle);
        rotation.ShowDialog();
        Check(rotation.Settings is {Rotate:true,Interval:90,Jitter:10},
            "Per-account character rotation settings were not saved by the editor.");

        window.UpdateLayout();
        var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
        image.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using(var stream=File.Create(output))encoder.Save(stream);
        window.Close();app.Shutdown();
        Console.WriteLine("ACCOUNT_UI_OK uniform_list manual_login account_template rotation_editor");
    }

    private static void Check(bool value,string message)
    {if(!value)throw new Exception(message);}
    private static void Pump(Application app)=>app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static Button FindButton(DependencyObject parent,string label)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is Button button && Equals(button.Content,label))return button;
            try{return FindButton(child,label);}catch(InvalidOperationException){}
        }
        throw new InvalidOperationException("Button not found: "+label);
    }
    private static TextBlock FindText(DependencyObject parent,string value)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is TextBlock text && text.Text.Contains(value))return text;
            try{return FindText(child,value);}catch(InvalidOperationException){}
        }
        throw new InvalidOperationException("Text not found: "+value);
    }
}
