using PriceCheck.Collector.Models;
using PriceCheck.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async Task<bool> TickClientRecoveryAsync(ProfileRuntime runtime)
    {
        if(runtime.Session is not { } session) return false;
        var request=_clientRecovery.Observe(runtime.Profile,session,runtime.ReaderAttached,runtime.IsCollectionEnabled,
            ClientHealthProbe.Read(session),runtime.ReaderAttached?runtime.Radar?.LivePlayerPositionAvailable:null,
            runtime.ClientFault,DateTimeOffset.UtcNow);
        if(request is null) return _clientRecovery.Pending(runtime.Profile.Id,session);
        runtime.IsBusy=true;
        var template=LaunchTemplates.FirstOrDefault(t=>t.Id==runtime.Profile.LaunchTemplateId && t.Id!=Guid.Empty);
        try
        {
            Log($"{runtime.Profile.Name}: automatic client restart — {request.Reason}");
            await _clientRecovery.RestartAsync(runtime.Profile,request,template,
                ()=>request.Collection ? _collection.SuspendForClientChangeAsync(runtime) : _collection.DetachAsync(runtime),async replacement=>
                {
                    runtime.Session=replacement;
                    runtime.Profile.LastProcessId=replacement.ProcessId;
                    runtime.Profile.LastProcessStartUtc=replacement.StartedAtUtc;
                    if(request.Reader) await _collection.AttachAsync(runtime,CancellationToken.None);
                    if(request.Collection) await _collection.SetCollectionAsync(runtime,true);
                    runtime.ClientFault=null;
                    _characterRotation.Started(runtime.Profile,replacement,DateTimeOffset.UtcNow);
                },status=> { runtime.LaunchStatus=runtime.Status=status; runtime.Protection=_launcher.Protection(runtime.Profile.Id); },CancellationToken.None,
                request.Reader ? CaptureReaderBeforeLogin(runtime) : null);
            runtime.RefreshProfile();
            Log($"{runtime.Profile.Name}: restarted PID {runtime.ProcessId}, character {runtime.Profile.CharacterSlot}; collection {runtime.IsCollectionEnabled}");
        }
        catch(Exception e)
        {
            // Keep the previous session reference for the bounded retry policy.
            runtime.ClientFault=$"Client restart failed: {e.GetBaseException().Message}";
            runtime.Status=runtime.LaunchStatus=runtime.ClientFault;
            Log($"{runtime.Profile.Name}: {runtime.ClientFault}");
        }
        finally
        {
            runtime.Protection=_launcher.Protection(runtime.Profile.Id);
            runtime.IsBusy=false;
            await SaveProfilesAsync();
            if(template?.RotateEachLaunch==true) await SaveTemplatesAsync();
            RefreshClientList();
        }
        return true;
    }
}
