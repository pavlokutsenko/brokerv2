using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PriceCheck.Collector;
using PriceCheck.Collector.Models;

internal static class Program
{
[STAThread]
private static void Main(string[] args)
{
if (args.Length != 1) throw new ArgumentException("Pass an output PNG path.");
var settingsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceCheckCollector");
var settingsBefore = new[] { "profiles.json", "launch-templates.json" }.ToDictionary(name => name,
    name => File.Exists(Path.Combine(settingsRoot, name)) ? File.ReadAllBytes(Path.Combine(settingsRoot, name)) : null);
var app = new App(enableRuntime: false);
app.InitializeComponent();
var view = new LaunchTemplatesView();
view.SetTemplates([new LaunchTemplate
{
    Name = "Gamma · rotating HWID",
    Identity = new LaunchIdentity
    {
        WorldIdentitySeed = "0123456789ABCDEF0123456789ABCDEF",
        SystemUuid = Guid.NewGuid().ToString("D"),
        MachineGuid = Guid.NewGuid().ToString("D"),
        BoardSerial = "A1B2C3D4", VolumeSerial = "1234ABCD", MacAddress = "02AABBCCDDEE",
        SystemSerial = "SYSTEM01", ChassisSerial = "CHASSIS01", ProcessorId = "AABBCCDDEEFF0011",
        ProcessorSerial = "CPU12345", MemorySerial = "MEM12345", DiskSerial = "DISK12345",
        HardwareProfileGuid = Guid.NewGuid().ToString("B"), WindowsProductId = "00330-12345-67890-AAOEM",
        DiskGuid = Guid.NewGuid().ToString("D"), DiskSignature = "1234ABCD",
        SusClientId = Guid.NewGuid().ToString("D"), VideoIdentifier = Guid.NewGuid().ToString("B"),
        RegistryComputerName = "PC-TEST", InstallDate = 1700000000, RouterMac = "02AABBCCDDEF"
    }
}]);
view.Measure(new Size(1024, 740));
view.Arrange(new Rect(0, 0, 1024, 740));
view.UpdateLayout();

var toggle = (CheckBox)view.FindName("ProxyToggle")!;
var fields = (StackPanel)view.FindName("ProxyFields")!;
var host = (TextBox)view.FindName("ProxyHostInput")!;
var port = (TextBox)view.FindName("ProxyPortInput")!;
var user = (TextBox)view.FindName("ProxyUserInput")!;
var password = (PasswordBox)view.FindName("TemplatePassword")!;
var save = (Button)view.FindName("SaveTemplatesButton")!;
if (fields.IsEnabled) throw new Exception("Proxy fields should start disabled.");
toggle.IsChecked = true;
view.UpdateLayout();
if (!fields.IsEnabled) throw new Exception($"Proxy fields did not enable when checked (toggle={toggle.IsChecked}, editor={((FrameworkElement)view.FindName("Editor")!).IsEnabled}, local={fields.ReadLocalValue(UIElement.IsEnabledProperty)}).");
host.Text = "127.0.0.1";
port.Text = "50100";
port.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
user.Text = "test-user";
password.Password = "test-password";
if (view.Templates[0].ProxyHost != "127.0.0.1" || view.Templates[0].ProxyPort != 50100 ||
    view.Templates[0].ProxyUser != "test-user" || view.Templates[0].ProxyPassword != "test-password")
    throw new Exception("Proxy input did not update the selected template.");

IReadOnlyList<LaunchTemplate>? saved = null;
view.SaveRequested = values => { saved = values; return Task.CompletedTask; };
save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
if (saved?.Count != 1 || !saved[0].ProxyEnabled || saved[0].ProxyPort != 50100)
    throw new Exception("Save did not receive the proxy configuration.");
if (saved[0].Identity.WorldIdentitySeed != "0123456789ABCDEF0123456789ABCDEF")
    throw new Exception("Template editing lost the saved world identity seed.");

view.Measure(new Size(1024, 740));
view.Arrange(new Rect(0, 0, 1024, 740));
view.UpdateLayout();
var bitmap = new RenderTargetBitmap(1024, 740, 96, 96, PixelFormats.Pbgra32);
bitmap.Render(view);
var encoder = new PngBitmapEncoder();
encoder.Frames.Add(BitmapFrame.Create(bitmap));
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
using (var stream = File.Create(args[0])) encoder.Save(stream);
Console.WriteLine("COLLECTOR_UI_OK proxy_toggle=enabled proxy_saved=true");
ModuleViews.VerifyAndRender(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
app.Shutdown();
foreach (var (name, before) in settingsBefore)
{
    var path = Path.Combine(settingsRoot, name);
    if (before is null ? File.Exists(path) : !File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(before))
        throw new Exception("UI smoke modified production settings.");
}
Console.WriteLine("PRODUCTION_SETTINGS_UNCHANGED");
}
}
