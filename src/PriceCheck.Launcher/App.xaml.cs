using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using PriceCheck.Windows;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Launcher;

public partial class App : Application
{
    private readonly bool _enableRuntime;
    private DesktopInstanceLease? _instance;
    public App() : this(true) { }
    public App(bool enableRuntime) => _enableRuntime = enableRuntime;
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!_enableRuntime) { ShutdownMode = ShutdownMode.OnExplicitShutdown; return; }
        base.OnStartup(e);
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        try
        {
            _instance = new("Launcher");
            if (!_instance.Acquired)
            {
                MessageBox.Show("PriceCheck Launcher is already running.", "PriceCheck Launcher");
                Shutdown(); return;
            }
            new DriverBootstrapper().EnsureReady();
            const string prefix = "--launch-profiles=";
            MainWindow = new MainWindow
            {
                StartupProfiles = e.Args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..]
            };
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "PriceCheck Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }
}
