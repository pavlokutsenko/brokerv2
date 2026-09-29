using System.Windows;
using PriceCheck.Collector.Models;
using PriceCheck.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async Task EnsureReaderAttachedAsync(ProfileRuntime runtime)
    {
        if (runtime.Session is not { } session || !ClientProcessIdentity.IsCurrent(session) ||
            !_launcher.Owns(runtime.Profile.Id, session))
            throw new InvalidOperationException("Сначала запустите клиент выбранного профиля.");
        if (runtime.ReaderAttached) return;
        if (Runtimes.Any(other => other != runtime && other.Session?.ProcessId == session.ProcessId))
            throw new InvalidOperationException("Этот клиент уже принадлежит другому профилю.");
        await _launcher.ValidateProtectionAsync(runtime.Profile.Id, false, CancellationToken.None);
        await _collection.AttachAsync(runtime, CancellationToken.None);
    }

    private Task RefreshSelectedAsync() => SelectedRuntime is null ? Task.CompletedTask : RefreshRuntimesAsync([SelectedRuntime]);
    private Task RefreshAllAsync() => RefreshRuntimesAsync(Runtimes.ToArray());

    private async Task RefreshRuntimesAsync(IReadOnlyList<ProfileRuntime> runtimes)
    {
        if (_refreshing || _closing) return;
        _refreshing = true;
        try
        {
            foreach (var runtime in runtimes)
            {
                if (runtime.IsBusy) continue;
                if (await TickProtectionAsync(runtime)) continue;
                if(await TickClientRecoveryAsync(runtime)) continue;
                if(await TickCharacterRotationAsync(runtime)) continue;
                await _collection.RefreshAsync(runtime);
                if (runtime.Session is { } session && !ClientProcessIdentity.IsCurrent(session))
                {
                    _launcher.ReleaseExited(runtime.Profile.Id);
                    _characterRotation.Forget(runtime.Profile.Id);
                    runtime.Session = null;
                    runtime.Profile.LastProcessId = null;
                    runtime.Profile.LastProcessStartUtc = null;
                    runtime.LaunchStatus = "Client exited";
                    await SaveProfilesAsync();
                }
            }
        }
        catch (Exception exception) { Log($"Module refresh: {exception.GetBaseException().Message}"); }
        finally { _refreshing = false; }
    }
}
