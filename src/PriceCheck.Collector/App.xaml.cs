using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using PriceCheck.Collector.Runtime.Driver;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        if (e.Args.Length >= 1 && e.Args[0].Equals("--upload-worker", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var directory = e.Args.Length >= 2 ? e.Args[1] : ServerUploadOutbox.DirectoryPath;
            _ = Task.Run(() => ServerUploadWorker.RunAsync(directory)).ContinueWith(
                task =>
                {
                    if (task.Exception is not null)
                    {
                        try
                        {
                            Directory.CreateDirectory(directory);
                            File.AppendAllText(
                                Path.Combine(directory, "worker-errors.log"),
                                $"{DateTimeOffset.UtcNow:O} {task.Exception.GetBaseException()}\n");
                        }
                        catch { }
                    }
                    Dispatcher.Invoke(() => Shutdown());
                }, TaskScheduler.Default);
            return;
        }
        base.OnStartup(e);
        try
        {
            new DriverBootstrapper().EnsureReady();
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Не удалось загрузить LU4Memory",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
