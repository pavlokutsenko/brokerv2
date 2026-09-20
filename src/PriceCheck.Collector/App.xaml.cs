using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Collector;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        base.OnStartup(e);
        try
        {
            new DriverBootstrapper().EnsureReady();
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
