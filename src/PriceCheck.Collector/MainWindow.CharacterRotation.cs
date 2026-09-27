using PriceCheck.Collector.Models;
using PriceCheck.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async Task<bool> TickCharacterRotationAsync(ProfileRuntime runtime)
    {
        var now=DateTimeOffset.UtcNow;
        try
        {
            runtime.CharacterRotationStatus=_characterRotation.Status(runtime.Profile,runtime.Session,now);
            if(runtime.Session is not { } session || !ClientProcessIdentity.IsCurrent(session) ||
                !_characterRotation.IsDue(runtime.Profile,session,now)) return false;
            runtime.IsBusy=true;
            var resumeReader=runtime.ReaderAttached;var resumeCollection=runtime.IsCollectionEnabled;
            var template=LaunchTemplates.FirstOrDefault(t=>t.Id==runtime.Profile.LaunchTemplateId && t.Id!=Guid.Empty);
            try
            {
                await _characterRotation.RotateAsync(runtime.Profile,session,template,
                    ()=>resumeCollection ? _collection.SuspendForClientChangeAsync(runtime) : _collection.DetachAsync(runtime),async replacement=>
                    {
                        runtime.Session=replacement;
                        runtime.Profile.LastProcessId=replacement.ProcessId;
                        runtime.Profile.LastProcessStartUtc=replacement.StartedAtUtc;
                        if(resumeReader) await _collection.AttachAsync(runtime,CancellationToken.None);
                        if(resumeCollection) await _collection.SetCollectionAsync(runtime,true);
                    },status=>runtime.CharacterRotationStatus=runtime.LaunchStatus=status,CancellationToken.None,
                    resumeReader ? CaptureReaderBeforeLogin(runtime) : null);
                runtime.RefreshProfile();
                OnPropertyChanged(nameof(CharacterOptions));
                Log($"{runtime.Profile.Name}: changed to character {runtime.Profile.CharacterSlot} of {runtime.Profile.RotationCharacterCount}");
            }
            catch(Exception e)
            {
                if(!ClientProcessIdentity.IsCurrent(session))
                {
                    _clientRecovery.Arm(runtime.Profile,session,resumeReader,resumeCollection);
                    if(_clientRecovery.Pending(runtime.Profile.Id,session))
                    { runtime.Session=session;runtime.ClientFault=$"Character change failed: {e.GetBaseException().Message}"; }
                }
                throw;
            }
            finally
            {
                if(runtime.Session is { } old && !ClientProcessIdentity.IsCurrent(old) && !_clientRecovery.Pending(runtime.Profile.Id,old))
                { runtime.Session=null;runtime.Profile.LastProcessId=null;runtime.Profile.LastProcessStartUtc=null; }
                runtime.IsBusy=false;
                await SaveProfilesAsync();
                if(template?.RotateEachLaunch==true) await SaveTemplatesAsync();
                RefreshClientList();
            }
            return true;
        }
        catch(Exception e)
        {
            var message=e.GetBaseException().Message;
            if(runtime.CharacterRotationStatus!=message) Log($"{runtime.Profile.Name}: character rotation — {message}");
            runtime.CharacterRotationStatus=message;
            return false;
        }
    }
}
