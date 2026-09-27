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
        window.Runtimes.Add(runtime);
        window.SelectedRuntime = runtime;
        var tabs = (TabControl)window.FindName("MainTabs");
        if (tabs.Items.Count != 3 || tabs.Items.Cast<TabItem>().Select(value => value.Header?.ToString()).SequenceEqual(new[] { "LAUNCH", "COLLECTION", "ЖУРНАЛ" }) == false)
            throw new Exception("Expected launch, collection and journal tabs.");
        var root = (FrameworkElement)window.Content;
        foreach (var width in new[] { 1380, 1120 })
        {
            for (var index = 0; index < 3; index++)
            {
                tabs.SelectedIndex = index;
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
        Console.WriteLine("MODULE_UI_OK launch_collection_journal shared_launch_views synthetic_only widths=1380,1120");
        window.Close();
    }
}
