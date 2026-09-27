using System.Windows;
using PriceCheck.Collector.Models;
using PriceCheck.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private void RefreshClients_Click(object sender, RoutedEventArgs e) => RefreshClientList();
    private void RefreshClientList()
    {
        var selected = SelectedClient;
        AvailableClients.Clear();
        foreach (var session in ClientProcessIdentity.Discover()) AvailableClients.Add(session);
        SelectedClient = AvailableClients.FirstOrDefault(value => value == selected) ?? AvailableClients.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedClient));
    }

    private async void AttachReader_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is not { IsBusy: false } runtime || _closing) return;
        runtime.IsBusy = true;
        try
        {
            var session = runtime.Session is { } current && ClientProcessIdentity.IsCurrent(current) ? current : SelectedClient;
            if (session is null) throw new InvalidOperationException("Launch a client or select an existing process.");
            await _launcher.ValidateProtectionAsync(runtime.Profile.Id, false, CancellationToken.None);
            if (Runtimes.Count(other => other != runtime && other.ReaderAttached) >= 4)
                throw new InvalidOperationException("Одновременно поддерживаются от 1 до 4 коллекторов.");
            if (Runtimes.Any(other => other != runtime && other.Session == session))
                throw new InvalidOperationException("This client already belongs to another profile.");
            runtime.Session = session;
            runtime.Status = "Connecting reader…";
            await _collection.AttachAsync(runtime, CancellationToken.None);
            runtime.Profile.LastProcessId = session.ProcessId;
            runtime.Profile.LastProcessStartUtc = session.StartedAtUtc;
            await SaveProfilesAsync();
        }
        catch (Exception exception) { runtime.Status = "Reader connection failed"; ShowModuleError(runtime, exception); }
        finally { runtime.IsBusy = false; }
    }

    private async void DetachReader_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is not { IsBusy: false } runtime) return;
        runtime.IsBusy = true;
        try { _clientRecovery.Forget(runtime.Profile.Id);runtime.ClientFault=null;await _collection.DetachAsync(runtime); await SaveProfilesAsync(); }
        catch (Exception exception) { ShowModuleError(runtime, exception); }
        finally { runtime.IsBusy = false; }
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
