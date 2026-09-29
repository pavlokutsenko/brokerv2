using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

// Test-only hook: execute the real portable EXE, then exercise its WPF window
// without starting a driver, a game, uploads, or production configuration saves.
public static class StartupHook
{
    public static void Initialize()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { CheckHost(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null)
        {
            File.WriteAllText(OutputPath(), JsonSerializer.Serialize(new { Error = failure.ToString() }));
            Environment.Exit(1);
        }
        Environment.Exit(0);
    }

    private static string OutputPath() => Environment.GetEnvironmentVariable("PRICECHECK_PACKAGE_PROBE_OUTPUT")
        ?? throw new InvalidOperationException("Missing probe output path.");

    private static void CheckHost()
    {
        var entry = Assembly.GetEntryAssembly() ?? throw new InvalidOperationException("Missing entry assembly.");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path.");
        var product = Path.GetFileNameWithoutExtension(executable).Split('.').Last();
        if (product is not ("Launcher" or "Collector")) throw new InvalidOperationException("Wrong entry point.");
        var root = Path.GetDirectoryName(executable)!;
        var payload = Path.Combine(root, "runtime");
        if (!string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), payload, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(entry.Location, Path.Combine(payload, $"PriceCheck.{product}.dll"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Portable apphost did not bind to the runtime directory.");
        foreach (var path in new[] { "DriverRuntime/lu4_memory_wfp.sys", "ClientLaunchRuntime/PriceCheck.ClientAgent.dll", "ClientLaunchRuntime/PriceCheck.ClientLogin.dll" })
            if (!File.Exists(Path.Combine(payload, path))) throw new FileNotFoundException("Runtime lookup failed.", path);
        if (product == "Collector" && !File.Exists(Path.Combine(payload, "BrokerRuntime", "BrokerWorker.exe")))
            throw new FileNotFoundException("Collector worker lookup failed.");
        var appType = entry.GetType($"PriceCheck.{product}.App", true)!;
        var app = (Application)Activator.CreateInstance(appType, [false])!;
        appType.GetMethod("InitializeComponent")!.Invoke(app, null);
        var windowType = entry.GetType($"PriceCheck.{product}.MainWindow", true)!;
        var arguments = new object?[] { false, Path.Combine(Path.GetDirectoryName(OutputPath())!, "settings") };
        var window = (Window)Activator.CreateInstance(windowType, arguments)!;
        window.ShowInTaskbar = false; window.Left = -20000; window.Top = -20000;
        window.Show(); app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (!window.IsLoaded || window.FindName("LaunchPanel") is null || window.ActualWidth <= 0)
            throw new InvalidOperationException("Portable WPF window did not load.");
        var title = window.Title;
        window.Close(); app.Shutdown();
        File.WriteAllText(OutputPath(), JsonSerializer.Serialize(new { Product = product, Executable = executable,
            BaseDirectory = AppContext.BaseDirectory, EntryAssembly = entry.Location, Title = title, WpfLoaded = true }));
    }
}
