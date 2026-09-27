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
        var app=new App(enableRuntime:false);app.InitializeComponent();
        var runtime=new ProfileRuntime{Profile=new(){AutoLoginEnabled=true,RotationCharacterCount=7},
            CharacterRotationStatus="Character 0 of 7 · next change 17:30:00 · 60.0 min",LaunchStatus="Client ready"};
        var window=new MainWindow(false){ShowInTaskbar=false,Left=-20000,Top=-20000};
        window.Runtimes.Add(runtime);window.SelectedRuntime=runtime;
        window.Show();app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var view=(LaunchPanelView)window.FindName("LaunchPanel")!;
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
        runtime.CharacterRotationStatus="Character 0 of 7 · next change 17:30:00 · 60.0 min";
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
}
