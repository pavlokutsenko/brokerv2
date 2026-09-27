using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Launcher;

internal static class Program
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    [STAThread] private static void Main(string[] args)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var production = new[] { "PriceCheckCollector", "PriceCheckLauncher" }
            .SelectMany(product => new[] { "profiles.json", "launch-templates.json" }.Select(file => Path.Combine(local, product, file)))
            .ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        var output = Path.GetFullPath(args.Single()); Directory.CreateDirectory(output);
        var settings = Path.Combine(output, "settings"); Directory.CreateDirectory(settings);
        var profileId = Guid.NewGuid(); var templateId = Guid.NewGuid();
        const string seed = "0123456789ABCDEF0123456789ABCDEF";
        var profile = new CollectorProfile { Id = profileId, Name = "Gamma", LaunchTemplateId = templateId };
        new ProfileStore(settings).SaveAsync([profile]).GetAwaiter().GetResult();
        var templateStore = new LaunchTemplateStore(settings);
        templateStore.SaveAsync([new() { Id = templateId, Name = "Fixture", HardwareEnabled = true,
            Identity = new() { WorldIdentitySeed = seed } }]).GetAwaiter().GetResult();
        var references = typeof(MainWindow).Assembly.GetReferencedAssemblies()
            .Concat(typeof(PriceCheck.Launching.LaunchModule).Assembly.GetReferencedAssemblies())
            .Concat(typeof(PriceCheck.Collector.LaunchPanelView).Assembly.GetReferencedAssemblies());
        Check(!references.Any(value => value.Name is "PriceCheck.Collection" or "PriceCheck.Collector"), "Launcher depends on collection or its desktop host.");
        var app = new App(false); app.InitializeComponent();
        var window = new MainWindow(true, settings) { ShowInTaskbar = false, Left = -20000, Top = -20000 };
        window.Events.CollectionChanged += (_, e) => { foreach (var value in e.NewItems ?? Array.Empty<object>()) Console.WriteLine(value); };
        window.Show(); Pump(app);
        Console.WriteLine("LAUNCHER_UI_LOADED");
        Check(window.Runtimes.Count == 1 && window.SelectedRuntime!.Profile.Id == profileId, "Launcher did not load the configured profile.");
        Check(window.SelectedRuntime!.Profile.LaunchTemplateId == templateId, "Launcher changed the template binding.");
        Check(window.SelectedRuntime.Session is null, "Launcher adopted a saved game PID.");
        var tabs = (TabControl)window.FindName("MainTabs")!;
        Check(tabs.Items.Count == 3 && !tabs.Items.Cast<TabItem>().Any(value => value.Header.ToString()!.Contains("COLLECTION")), "Launcher displays collection UI.");
        var panel = (PriceCheck.Collector.LaunchPanelView)window.FindName("LaunchPanel")!;
        var hwid = (TextBlock)panel.FindName("HardwareProtectionIndicator")!;
        var proxy = (TextBlock)panel.FindName("ProxyProtectionIndicator")!;
        Check(hwid.Text.Contains("не проверен") && proxy.Text.Contains("не проверен"), "Unverified indicators displayed success.");
        window.SelectedRuntime.Protection = new(true, true, true, 2, 69, 13, "FIXTURE123", null); Pump(app);
        Check(hwid.Text.Contains("FIXTURE123") && proxy.Text.Contains("2 CONNECT"), "Protection status did not bind in Launcher.");
        Render(window, Path.Combine(output, "launcher-protected.png"));
        window.SelectedRuntime.Protection = window.SelectedRuntime.Protection with { Error = "Synthetic protection failure" }; Pump(app);
        Check(hwid.Text.Contains("ОШИБКА") && proxy.Text.Contains("ОШИБКА"), "Failed protection displayed success.");
        window.SelectedRuntime.Protection = PriceCheck.Contracts.ClientProtectionStatus.Pending; Pump(app);
        var login = (CheckBox)FindVisual<CheckBox>(panel, value => Equals(value.Content, "Auto login"));
        login.IsChecked = true; Pump(app);
        Check(new ProfileStore(settings).LoadAsync().GetAwaiter().GetResult().Single().AutoLoginEnabled, "Launcher profile changes were not saved.");
        var templates = (PriceCheck.Collector.LaunchTemplatesView)window.FindName("TemplatesView")!;
        tabs.SelectedItem = window.FindName("TemplatesTab"); Pump(app);
        ((CheckBox)templates.FindName("ProxyToggle")!).IsChecked = true;
        ((TextBox)templates.FindName("ProxyHostInput")!).Text = "127.0.0.1";
        var port = (TextBox)templates.FindName("ProxyPortInput")!;
        port.Text = "50100"; port.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        ((TextBox)templates.FindName("ProxyUserInput")!).Text = "fixture-user";
        ((PasswordBox)templates.FindName("TemplatePassword")!).Password = "fixture-password";
        ClientLaunchConfiguration.ValidateIdentity(templates.Templates.Single().Identity);
        ClientLaunchConfiguration.ValidateProxy(templates.Templates.Single());
        ((Button)templates.FindName("SaveTemplatesButton")!).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(app);
        var saved = templateStore.LoadAsync().GetAwaiter().GetResult().Single();
        Check(saved.ProxyEnabled && saved.ProxyPort == 50100 && saved.Identity.WorldIdentitySeed == seed, "Proxy editing did not save or changed HWID seed.");
        tabs.SelectedIndex = 0; Pump(app); Render(window, Path.Combine(output, "launcher-1380.png"));
        window.Width = 1120; Pump(app); Render(window, Path.Combine(output, "launcher-1120.png"));
        window.Close(); Pump(app); Check(!window.IsVisible, "Launcher did not close when idle."); app.Shutdown();
        Check(new ProfileStore(settings).LoadAsync().GetAwaiter().GetResult().Single().Id == profileId, "Profile identity changed on save/close.");
        foreach (var (path, bytes) in production)
            Check(bytes is null ? !File.Exists(path) : File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes), "UI test changed production settings: " + path);
        Console.WriteLine("LAUNCHER_SMOKE_OK independent_assemblies isolated_settings stable_ids stable_seed proxy_save ui_1380_1120 idle_close production_unchanged");
    }
    private static void Pump(Application app) => app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static FrameworkElement FindVisual<T>(DependencyObject parent, Func<T, bool> matches) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T element && matches(element)) return element;
            try { return FindVisual(child, matches); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Control was not found.");
    }
    private static void Render(Window window, string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
