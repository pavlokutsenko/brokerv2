using PriceCheck.Launching;
using PriceCheck.Windows;

namespace PriceCheck.Launcher;

public partial class MainWindow
{
    private async Task RefreshAsync()
    {
        if (_refreshing || _closing) return;
        _refreshing = true;
        try
        {
            foreach (var runtime in AllAccounts().ToArray())
            {
                if (runtime.IsBusy || runtime.Session is not { } session) continue;
                runtime.Protection = _launcher.Protection(runtime.Profile.Id);
                if (runtime.Protection.Failed)
                {
                    _recovery.Forget(runtime.Profile.Id); _rotation.Forget(runtime.Profile.Id);
                    _launcher.Stop(runtime.Profile.Id);
                    ClearExited(runtime);
                    runtime.ClientFault = runtime.Protection.Error;
                    runtime.LaunchStatus = runtime.ClientFault!;
                    Log(runtime.ClientFault!, runtime);
                    await SaveProfilesAsync();
                    continue;
                }
                var now = DateTimeOffset.UtcNow;
                runtime.CharacterRotationStatus = _rotation.Status(runtime.Profile, session, now);
                var request = _recovery.Observe(runtime.Profile, session, false, false, ClientHealthProbe.Read(session), null, runtime.ClientFault, now);
                if (request is null && _recovery.Pending(runtime.Profile.Id, session)) continue;
                if (request is null && !ClientProcessIdentity.IsCurrent(session)) { ClearExited(runtime); runtime.LaunchStatus = "Клиент закрыт"; await SaveProfilesAsync(); continue; }
                if (request is null && !_rotation.IsDue(runtime.Profile, session, now)) continue;
                var template = LaunchTemplates.FirstOrDefault(value => value.Id == runtime.Profile.LaunchTemplateId && value.Id != Guid.Empty);
                runtime.IsBusy = true;
                try
                {
                    Task Restore(PriceCheck.Contracts.ClientSession replacement)
                    {
                        SetSession(runtime, replacement); _rotation.Started(runtime.Profile, replacement, DateTimeOffset.UtcNow);
                        Changed(nameof(CharacterOptions)); return Task.CompletedTask;
                    }
                    if (request is not null)
                        await _recovery.RestartAsync(runtime.Profile, request, template, () => Task.CompletedTask, Restore,
                            status => { runtime.LaunchStatus = status; runtime.Protection = _launcher.Protection(runtime.Profile.Id); }, CancellationToken.None);
                    else
                        await _rotation.RotateAsync(runtime.Profile, session, template, () => Task.CompletedTask, Restore,
                            status => { runtime.LaunchStatus = status; runtime.Protection = _launcher.Protection(runtime.Profile.Id); }, CancellationToken.None);
                    Log($"{runtime.ProcessLabel} · {(request is null ? "персонаж сменён" : "перезапущен")}", runtime);
                }
                catch (Exception exception)
                {
                    runtime.ClientFault = exception.GetBaseException().Message;
                    if (exception is PriceCheck.Collector.Services.LaunchProtectionException) _recovery.Forget(runtime.Profile.Id);
                    else _recovery.Arm(runtime.Profile, session, false, false);
                    Log(runtime.ClientFault, runtime); runtime.LaunchStatus = runtime.ClientFault;
                }
                finally { runtime.Protection = _launcher.Protection(runtime.Profile.Id); runtime.IsBusy = false; await SaveProfilesAsync(); await SaveRotatedTemplateAsync(template); }
            }
        }
        catch (Exception exception) { Log(exception.GetBaseException().Message); }
        finally { RefreshAccountCounts();_refreshing = false; }
    }
}
