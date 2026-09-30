using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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
        var profile = new CollectorProfile { Id = profileId, Name = "Gamma", LoginName="fixture-account", LaunchTemplateId = templateId };
        new ProfileStore(settings).SaveAsync([profile]).GetAwaiter().GetResult();
        var templateStore = new LaunchTemplateStore(settings);
        templateStore.SaveAsync([new() { Id = templateId, Name = "Fixture", HardwareEnabled = true,
            Identity = new() { WorldIdentitySeed = seed } }]).GetAwaiter().GetResult();
        var firstMigration = templateStore.LoadAsync().GetAwaiter().GetResult().Single().Identity;
        var secondMigration = templateStore.LoadAsync().GetAwaiter().GetResult().Single().Identity;
        Check(firstMigration.ProcessorModel == secondMigration.ProcessorModel &&
            firstMigration.ProcessorRevision == secondMigration.ProcessorRevision &&
            firstMigration.SqmMachineId == secondMigration.SqmMachineId,
            "New identity fields change between loads of a fixed template.");
        var inventory = Task.Run(HardwareInventoryService.Scan).GetAwaiter().GetResult();
        foreach (var error in inventory.Where(row => row.Coverage is "Scan unavailable" or "Access denied"))
            Console.WriteLine($"INVENTORY_UNAVAILABLE {error.Label}: {error.CurrentValue}");
        Check(inventory.Any(row => row.Coverage == "SetupAPI/CM · hooked"),
            "USB/HID API inventory is unavailable.");
        Check(inventory.Any(row => row.Label == "WMI · System UUID" &&
            row.Coverage == "IWbemClassObject::Get · hooked"),
            "WMI identity inventory is unavailable.");
        var preview = inventory.Select(row => IdentityPreviewService.WithTarget(row,
            new LaunchTemplate { Identity = firstMigration }, 0)).ToArray();
        Check(preview.Any(row => row.Coverage == "SetupAPI/CM · hooked" &&
            row.TargetValue != row.CurrentValue) &&
            preview.Any(row => row.Label == "WMI · System UUID" &&
                row.TargetValue == firstMigration.SystemUuid),
            "USB/HID or WMI preview does not match the selected template.");
        var references = typeof(MainWindow).Assembly.GetReferencedAssemblies()
            .Concat(typeof(PriceCheck.Launching.LaunchModule).Assembly.GetReferencedAssemblies())
            .Concat(typeof(PriceCheck.Collector.AccountLaunchPanelView).Assembly.GetReferencedAssemblies());
        Check(!references.Any(value => value.Name is "PriceCheck.Collection" or "PriceCheck.Collector"), "Launcher depends on collection or its desktop host.");
        var app = new App(false); app.InitializeComponent();
        VerifyEmptyTemplateEditor(app, output);
        var window = new MainWindow(true, settings) { ShowInTaskbar = false, Left = -20000, Top = -20000 };
        window.Events.CollectionChanged += (_, e) => { foreach (var value in e.NewItems ?? Array.Empty<object>()) Console.WriteLine(value); };
        window.Show(); Pump(app);
        Console.WriteLine("LAUNCHER_UI_LOADED");
        Check(window.Runtimes.Count == 1 && window.SelectedRuntime!.Profile.Id == profileId, "Launcher did not load the configured profile.");
        Check(window.SelectedRuntime!.Profile.LaunchTemplateId == templateId, "Launcher changed the template binding.");
        Check(window.SelectedRuntime.Session is null, "Launcher adopted a saved game PID.");
        var otherProfile = new CollectorProfile { Name = "Gamma", LoginServerName = "Gamma", LoginServerId = 1 };
        window.Events.Add(new(DateTimeOffset.UtcNow, profileId, "Gamma", "Gamma event"));
        window.Events.Add(new(DateTimeOffset.UtcNow, otherProfile.Id, "Gamma", "Second account event"));
        Check(window.ProfileEvents.Cast<LauncherJournalEntry>().Single().Message == "Gamma event",
            "Launcher log displayed another profile's event.");
        var otherRuntime = new LaunchRuntime { Profile = otherProfile };
        window.Runtimes.Add(otherRuntime); window.SelectedRuntime = otherRuntime; Pump(app);
        Check(window.ProfileEvents.Cast<LauncherJournalEntry>().Single().Message == "Second account event",
            "Launcher log did not follow the selected profile.");
        window.SelectedRuntime = window.Runtimes.First(value => value.Profile.Id == profileId);
        window.Runtimes.Remove(otherRuntime); Pump(app);
        var tabs = (TabControl)window.FindName("MainTabs")!;
        var profileTabs = (TabControl)window.FindName("ProfileTabs")!;
        Check(tabs.Items.Count == 2 && profileTabs.Items.Count == 2 &&
            !profileTabs.Items.Cast<TabItem>().Any(value => value.Header.ToString()!.Contains("Сбор")),
            "Launcher should show profile launch/log and shared templates, without collection UI.");
        var panel = (PriceCheck.Collector.AccountLaunchPanelView)window.FindName("LaunchPanel")!;
        Check(window.AccountItems.Count==1 && window.AccountItems[0].TemplateName=="Fixture",
            "Launcher did not show the account and its own template.");
        var hwid = (TextBlock)FindVisual<TextBlock>(panel,value=>value.Text.Contains("HWID"));
        var proxy = (TextBlock)FindVisual<TextBlock>(panel,value=>value.Text.Contains("ПРОКСИ"));
        Check(hwid.Text.Contains("не проверен") && proxy.Text.Contains("не проверен"), "Unverified indicators displayed success.");
        window.SelectedRuntime.Protection = new(true, true, true, 2, 69, 13, "FIXTURE123", null); Pump(app);
        Check(hwid.Text.Contains("FIXTURE123") && proxy.Text.Contains("2 CONNECT"), "Protection status did not bind in Launcher.");
        Render(window, Path.Combine(output, "launcher-protected.png"));
        window.SelectedRuntime.Protection = window.SelectedRuntime.Protection with { Error = "HWID: Synthetic protection failure" }; Pump(app);
        Check(hwid.Text.Contains("ОШИБКА") && proxy.Text.Contains("ОШИБКА"), "Failed protection displayed success.");
        window.SelectedRuntime.Protection = PriceCheck.Contracts.ClientProtectionStatus.Pending; Pump(app);
        Check(!new ProfileStore(settings).LoadAsync().GetAwaiter().GetResult().Single().AutoLoginEnabled,
            "Account launch unexpectedly enabled automatic login.");
        var templates = (PriceCheck.Collector.LaunchTemplatesView)window.FindName("TemplatesView")!;
        tabs.SelectedItem = window.FindName("TemplatesTab"); Pump(app);
        var scanStatus = (TextBlock)templates.FindName("ScanStatusText")!;
        var scanDeadline = DateTime.UtcNow.AddSeconds(15);
        while (!scanStatus.Text.StartsWith("Найдено:") && DateTime.UtcNow < scanDeadline)
        { Pump(app); Thread.Sleep(20); }
        Check(templates.ScanRows.Count > 0 && scanStatus.Text.StartsWith("Найдено:"), "Opening templates did not complete the automatic hardware scan.");
        Check(templates.ScanRows.Any(row => row.RegistryValueName == "MachineGuid" &&
            row.TargetValue == templates.Templates.Single().Identity.MachineGuid), "Automatic scan preview did not use the selected template.");
        ((Expander)templates.FindName("ScanDetails")!).IsExpanded = true; Pump(app);
        Render(window, Path.Combine(output, "launcher-hardware-scan.png"));
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
        Check(saved.Identity.ProcessorModel.Length > 0 && saved.Identity.ProcessorRevision.Length == 16 &&
            Guid.TryParse(saved.Identity.SqmMachineId, out _), "New identity fields were lost by the template editor.");
        tabs.SelectedIndex = 0; Pump(app); Render(window, Path.Combine(output, "launcher-1380.png"));
        window.Width = 1120; Pump(app); Render(window, Path.Combine(output, "launcher-1120.png"));
        window.Close(); Pump(app); Check(!window.IsVisible, "Launcher did not close when idle."); app.Shutdown();
        Check(new ProfileStore(settings).LoadAsync().GetAwaiter().GetResult().Single().Id == profileId, "Profile identity changed on save/close.");
        foreach (var (path, bytes) in production)
            Check(bytes is null ? !File.Exists(path) : File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes), "UI test changed production settings: " + path);
        Console.WriteLine("LAUNCHER_SMOKE_OK independent_assemblies isolated_settings stable_ids stable_seed proxy_save ui_1380_1120 idle_close production_unchanged");
    }
    private static void Pump(Application app) => app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void VerifyEmptyTemplateEditor(Application app, string output)
    {
        var settings = Path.Combine(output, "empty-settings-" + Guid.NewGuid().ToString("N"));
        var store = new LaunchTemplateStore(settings);
        var window = new MainWindow(true, settings) { ShowInTaskbar = false, Left = -20000, Top = -20000 };
        window.Show(); Pump(app);
        var tabs = (TabControl)window.FindName("MainTabs")!;
        tabs.SelectedItem = window.FindName("TemplatesTab"); Pump(app);
        var view = (PriceCheck.Collector.LaunchTemplatesView)window.FindName("TemplatesView")!;
        var editor = (StackPanel)view.FindName("Editor")!;
        var empty = (StackPanel)view.FindName("EmptyTemplatesPanel")!;
        var name = (TextBox)view.FindName("TemplateNameInput")!;
        var host = (TextBox)view.FindName("ProxyHostInput")!;
        Check(view.Templates.Count == 1 && editor.IsEnabled && name.IsEnabled && name.IsHitTestVisible,
            "First-run template editor does not accept input.");
        Check(store.LoadAsync().GetAwaiter().GetResult().Count == 0, "Draft template was saved before user confirmation.");
        ClientLaunchConfiguration.ValidateIdentity(view.Templates.Single().Identity);
        Render(window, Path.Combine(output, "launcher-first-template.png"));
        foreach (var control in new[] { "HardwareToggle", "RotateIdentityToggle", "ProxyToggle" })
        {
            var toggle = (CheckBox)view.FindName(control)!;
            var previous = toggle.IsChecked;
            var provider = (IToggleProvider)new CheckBoxAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)!;
            provider.Toggle(); Pump(app);
            Check(toggle.IsChecked != previous, control + " did not respond to an enabled toggle action.");
        }
        Check(!view.Templates[0].HardwareEnabled && view.Templates[0].RotateEachLaunch && view.Templates[0].ProxyEnabled,
            "First-run checkbox changes did not update the draft.");
        Check(host.IsEnabled && host.IsHitTestVisible && host.Focus(), "Proxy input did not become editable/focusable.");
        name.Text = "First template"; host.Text = "127.0.0.1";
        var port = (TextBox)view.FindName("ProxyPortInput")!;
        port.Text = "50100"; port.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        ((TextBox)view.FindName("ProxyUserInput")!).Text = "first-user";
        ((PasswordBox)view.FindName("TemplatePassword")!).Password = "first-password";
        ClientLaunchConfiguration.ValidateProxy(view.Templates.Single());
        ((Button)view.FindName("SaveTemplatesButton")!).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(app);
        var saved = store.LoadAsync().GetAwaiter().GetResult().Single();
        Check(saved.Name == "First template" && saved.ProxyEnabled && saved.ProxyHost == "127.0.0.1" &&
            saved.ProxyPort == 50100 && saved.ProxyPassword == "first-password", "First-run input did not persist on Save.");
        view.SetTemplates([]); Pump(app);
        var newButton = (Button)view.FindName("NewTemplateButton")!;
        newButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(app);
        Check(view.Templates.Select(value => value.Name).Distinct().Count() == 2, "New templates received duplicate names.");
        Check(view.Templates.All(value => value.MemoryBudgetEnabled && value.MemoryBudgetMiB == 3072 &&
            !value.CpuBudgetEnabled && value.CpuBudgetPercent == 20),
            "New templates must default to a 3 GiB memory limit and an unlimited CPU.");
        var delete = (Button)view.FindName("DeleteTemplateButton")!;
        delete.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        delete.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(app);
        Check(view.Templates.Count == 0 && empty.IsVisible && !editor.IsVisible,
            "Deleting the last template left a visible, disabled editor.");
        Render(window, Path.Combine(output, "launcher-no-templates.png"));
        ((Button)view.FindName("CreateTemplateButton")!).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(app);
        Check(view.Templates.Count == 1 && editor.IsEnabled && editor.IsVisible && !empty.IsVisible,
            "Creating a template did not restore the editor.");
        window.Close(); Pump(app);
        Console.WriteLine("EMPTY_TEMPLATE_EDITOR_OK draft toggles input_focus save delete_last recreate unique_names");
    }
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
