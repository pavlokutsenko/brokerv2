using System.IO;
using System.Windows;
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
        var runtime=new ProfileRuntime { Profile=new() {CenterZonesByCity=new() { ["Giran"]=new(){X=81500,Y=148400} }},
            IsCollectionEnabled=true,Status="Reading prices",UploadStatus="Outbox: 1 waiting · 0 rejected",
            Cycle=new(){Phase="Чтение цен",Detail="Маршрут 42% · читаем соседние лавки на проходе",Active=1254,Checked=947,Pending=307,Deferred=8,Overdue=16,Cycles=3,
                PassNewFound=19,PassRadarPending=4,PassRead=27,
                RadarQueue=[new("NewShop","In pool","New radar shop","Awaiting exact read",0)],
                Queue=[new("NesterR","Ready","New listing","Never checked",0),
                    new("NoO","Unresolved","Подход заблокирован · повтор в конце прохода","2.1 h",2),new("MissKharkiv","Ready","Периодическая проверка","24.2 h",0)]}};
        var view=new CollectionPanelView();
        var window=new TestWindow{Width=1060,Height=780,Content=view,DataContext=new{SelectedRuntime=runtime},
            ShowInTaskbar=false,Left=-20000,Top=-20000,WindowStyle=WindowStyle.None};
        window.Show();app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);view.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
        using(var stream=File.Create(args[0])) encoder.Save(stream);
        window.Close();Console.WriteLine($"CYCLE_UI_RENDER_OK {args[0]}");
        var main=new MainWindow(false) { ShowInTaskbar=false, Left=-20000,Top=-20000 };
        main.Runtimes.Add(runtime);main.SelectedRuntime=runtime;
        var log=typeof(MainWindow).GetMethod("Log",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        log.Invoke(main,["Gamma: INFO trader=NewShop чтение завершено: 5 строк, 0.4 секунды"]);
        log.Invoke(main,["Gamma: WARNING trader=Fixture брокер неполный: replies=1900/1925 unbound=2"]);
        log.Invoke(main,["Gamma: ERROR token=secretvalue доставка недоступна; результат остаётся в outbox"]);
        if(main.JournalEntries.Any(entry=>entry.Message.Contains("secretvalue")))throw new Exception("Journal secret redaction failed");
        main.Show();((System.Windows.Controls.TabControl)main.FindName("MainTabs")).SelectedIndex=2;
        app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);main.UpdateLayout();
        var journalImage=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!,"journal-ui.png");
        var journal=new RenderTargetBitmap((int)main.ActualWidth,(int)main.ActualHeight,96,96,PixelFormats.Pbgra32);journal.Render(main);
        var journalEncoder=new PngBitmapEncoder();journalEncoder.Frames.Add(BitmapFrame.Create(journal));
        using(var stream=File.Create(journalImage))journalEncoder.Save(stream);
        main.Close();Console.WriteLine($"JOURNAL_UI_RENDER_OK {journalImage}");app.Shutdown();
    }
}
public sealed class TestWindow:Window
{
    public string[] MarketOptions=>["Gamma","Black","White","Carmine"];
    public string[] AvailableClients=>[];
    public object? SelectedClient {get;set;}
    public string[] Events=>["Broker complete: 1254 traders","NoO deferred for 5 minutes"];
}
