using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using PriceCheck.Collector.Runtime.Driver;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class App : Application
{
    private PriceCheck.Windows.DesktopInstanceLease? _instance;
    private readonly bool _enableRuntime;
    public App() : this(true) { }
    public App(bool enableRuntime) => _enableRuntime = enableRuntime;

    protected override void OnStartup(StartupEventArgs e)
    {
        // WPF queues this callback even without Run(); visual tests pump that queue.
        if (!_enableRuntime) { ShutdownMode = ShutdownMode.OnExplicitShutdown; return; }
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
            _instance = new("Collector");
            if (!_instance.Acquired)
            {
                MessageBox.Show("PriceCheck Collector уже запущен.", "PriceCheck Collector");
                Shutdown(); return;
            }
            new DriverBootstrapper().EnsureReady();
            const string launchProfilePrefix = "--launch-profile=";
            const string launchProfilesPrefix = "--launch-profiles=";
            const string collectProfilePrefix = "--collect-profile=";
            var startupProfile = e.Args.FirstOrDefault(argument =>
                argument.StartsWith(launchProfilePrefix, StringComparison.OrdinalIgnoreCase) ||
                argument.StartsWith(launchProfilesPrefix, StringComparison.OrdinalIgnoreCase));
            MainWindow = new MainWindow
            {
                StartupResumeCharacter = e.Args.Any(argument =>
                    argument.Equals("--resume-character", StringComparison.OrdinalIgnoreCase)),
                StartupLaunchProfileName = startupProfile is null ? null :
                    startupProfile.StartsWith(launchProfilesPrefix, StringComparison.OrdinalIgnoreCase)
                        ? startupProfile[launchProfilesPrefix.Length..]
                        : startupProfile[launchProfilePrefix.Length..],
                StartupCollectProfileId = e.Args.FirstOrDefault(argument =>
                    argument.StartsWith(collectProfilePrefix, StringComparison.OrdinalIgnoreCase))?[collectProfilePrefix.Length..]
            };
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                    "Could not load LU4Memory",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }
}
