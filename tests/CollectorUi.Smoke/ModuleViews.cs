using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PriceCheck.Collector;
using PriceCheck.Collector.Models;

internal static class ModuleViews
{
    public static void VerifyAndRender(string directory)
    {
        var window = new MainWindow(initializeRuntime: false);
        var profile = new CollectorProfile { Name = "Gamma", LaunchFile = @"C:\Games\LU4\LU4.exe", AutoLoginEnabled = true, LoginName = "example-account" };
        var runtime = new ProfileRuntime { Profile = profile, LaunchStatus = "Client ready", Status = "Reader disconnected · client remains open" };
        runtime.Radar = new RadarSnapshot { PlayerX = 82710, PlayerY = 148425,
            LivePlayerPositionAvailable = true, CapturedAtUtc = DateTimeOffset.UtcNow,
            CenterZoneConfigured = true, IsInsideCenterZone = true, CenterZoneX = 82710,
            CenterZoneY = 148425, CenterZoneRadius = 200,
            Traders = Enumerable.Range(0, 180).Select(index => {
                var x = 80800 + index % 15 * 240; var y = 147100 + index / 15 * 240;
                return new RadarPoint(index, $"Fixture{index}", index % 2 == 0 ? 1 : 3, x, y,
                    Math.Sqrt(Math.Pow(x - 82710, 2) + Math.Pow(y - 148425, 2)), true, DateTimeOffset.UtcNow);
            }).ToArray() };
        runtime.Cycle=new(){CurrentPriceTraders=150,PassRead=20,Pending=30};
        var fullRadar=runtime.Radar;
        if(runtime.CenterRadarTraderCount!="—" || runtime.CurrentRadarTraderCount!="180")throw new Exception("Packet radar was mistaken for a complete center count.");
        runtime.ConfirmCenterRadarTraderCount(1550,DateTimeOffset.UtcNow);
        if(runtime.CenterRadarTraderCount!=1550.ToString("N0"))throw new Exception("Confirmed center count was not displayed.");
        runtime.Radar=new(){LivePlayerPositionAvailable=true,WorldCharacterDataAvailable=true,PlayerX=83000,PlayerY=150000,
            CenterZoneConfigured=true,IsInsideCenterZone=false,Traders=runtime.Radar.Traders.Take(23).ToArray()};
        if(runtime.CenterRadarTraderCount!=1550.ToString("N0") || runtime.CurrentRadarTraderCount!="23")throw new Exception("Roaming overwrote the confirmed center count.");
        runtime.Radar=fullRadar;
        window.Runtimes.Add(runtime);
        window.SelectedRuntime = runtime;
        runtime.CharacterRotationStatus="Персонаж 5 из 7 · до смены 00:29:58 · в 00:15:30";
        var tabs = (TabControl)window.FindName("MainTabs");
        var profileTabs = (TabControl)window.FindName("ProfileTabs");
        var collectionPanel = (CollectionPanelView)window.FindName("CollectionPanel");
        ((TabControl)collectionPanel.FindName("CollectionTabs")).SelectedIndex = 2;
        if (tabs.Items.Count != 2 || tabs.Items.Cast<TabItem>().Select(value => value.Header?.ToString()).SequenceEqual(new[] { "Профили", "Шаблоны" }) == false ||
            profileTabs.Items.Cast<TabItem>().Select(value => value.Header?.ToString()).SequenceEqual(new[] { "Запуск", "Сбор", "Журнал" }) == false)
            throw new Exception("Expected profile launch/collection/log tabs and a shared templates tab.");
        var root = (FrameworkElement)window.Content;
        foreach (var width in new[] { 1380, 1120 })
        {
            for (var index = 0; index < 4; index++)
            {
                tabs.SelectedIndex = index == 3 ? 1 : 0;
                if (index < 3) profileTabs.SelectedIndex = index;
                root.Measure(new Size(width, 800));
                root.Arrange(new Rect(0, 0, width, 800));
                root.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, 800, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, $"module-{index}-{width}.png"));
                encoder.Save(stream);
            }
        }
        // Synthetic rendering must never have started reading or modified session identity.
        if (runtime.ReaderAttached || runtime.ProcessId is not null) throw new Exception("Rendering started a live module.");
        Console.WriteLine("MODULE_UI_OK profile_launch_collection_log shared_templates synthetic_only widths=1380,1120");
        window.Close();
    }
}
