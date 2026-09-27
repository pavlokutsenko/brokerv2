using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async Task<bool> TickProtectionAsync(ProfileRuntime runtime)
    {
        runtime.Protection = _launcher.Protection(runtime.Profile.Id);
        if (!runtime.Protection.Failed || runtime.Session is null) return false;
        var message = runtime.Protection.Error!;
        _clientRecovery.Forget(runtime.Profile.Id);
        _characterRotation.Forget(runtime.Profile.Id);
        _launcher.Stop(runtime.Profile.Id);
        runtime.ClientFault = message;
        await _collection.DetachAsync(runtime);
        runtime.Session = null;
        runtime.Profile.LastProcessId = null; runtime.Profile.LastProcessStartUtc = null;
        runtime.Status = runtime.LaunchStatus = message;
        ShowModuleError(runtime, new LaunchProtectionException(message));
        await SaveProfilesAsync();
        return true;
    }
}
