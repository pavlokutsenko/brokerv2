using PriceCheck.Contracts;
using PriceCheck.Windows;
using PriceCheck.Collector.Contracts;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Launching;

// Owns the game and its proxy for the entire client lifetime. No reader dependency.
public sealed class LaunchModule
{
    private readonly IClientProcessService _processes;
    private readonly Func<int, CollectorProfile, CancellationToken, Task> _login;
    private readonly Func<int, ClientSession?> _identity;
    private readonly Func<CancellationToken,Task> _restartPause;
    private readonly Dictionary<Guid, ClientSession> _owned = [];
    private readonly HashSet<Guid> _launching = [];
    private readonly Dictionary<Guid, ClientProtectionStatus> _lastProtection = [];

    public ClientProtectionStatus Protection(Guid profileId) =>
        _owned.TryGetValue(profileId, out var session) ? _processes.Protection(session.ProcessId) :
        _lastProtection.GetValueOrDefault(profileId, ClientProtectionStatus.Pending);

    public Task ValidateProtectionAsync(Guid profileId, bool requireWorld, CancellationToken token)
    {
        if (!_owned.TryGetValue(profileId, out var session) || _identity(session.ProcessId) != session)
            throw new LaunchProtectionException("Проверка невозможна: запустите клиент через это приложение с HWID.");
        return _processes.ValidateProtectionAsync(session.ProcessId, requireWorld, token);
    }

    public LaunchModule(IClientProcessService? processes = null,
        Func<int, CollectorProfile, CancellationToken, Task>? login = null,
        Func<int, ClientSession?>? identity = null,Func<CancellationToken,Task>? restartPause=null)
    {
        _processes = processes ?? new ClientProcessService();
        _login = login ?? new ClientLoginService().EnterAsync;
        _identity = identity ?? ClientProcessIdentity.Read;
        _restartPause=restartPause ?? (token=>Task.Delay(TimeSpan.FromSeconds(10),token));
    }

    public async Task<ClientSession> LaunchAsync(CollectorProfile profile, LaunchTemplate? template,
        Action<string> progress, CancellationToken cancellationToken,
        Func<ClientSession,CancellationToken,Task>? beforeLogin=null)
    {
        if (!_launching.Add(profile.Id)) throw new InvalidOperationException("Profile is already launching.");
        ClientSession? session = null;
        _lastProtection[profile.Id] = ClientProtectionStatus.Pending with { ProxyRequired = template?.ProxyEnabled == true };
        try
        {
            if (_owned.TryGetValue(profile.Id, out var current))
            {
                if (_identity(current.ProcessId) == current)
                {
                    session = current;
                    await _processes.ValidateProtectionAsync(current.ProcessId, false, cancellationToken);
                    return current;
                }
                ReleaseExited(profile.Id);
            }
            if (profile.AutoLoginEnabled) ClientLoginService.Validate(profile);
            progress("Launching client…");
            var pid = await LaunchProcessWithRetryAsync(profile,template,progress,cancellationToken);
            session = _identity(pid);
            if (session is null)
            {
                _processes.Release(pid);
                throw new InvalidOperationException("Client exited during launch.");
            }
            _owned.Add(profile.Id, session);
            progress("Waiting for game window…");
            await _processes.WaitForGameWindowAsync(pid, cancellationToken);
            await _processes.ActivateLateAgentAsync(pid, cancellationToken);
            progress(template?.ProxyEnabled == true ? "Проверка HWID и защиты прокси…" : "Проверка HWID…");
            await _processes.ValidateProtectionAsync(pid, false, cancellationToken);
            if(beforeLogin is not null) await beforeLogin(session,cancellationToken);
            if (profile.AutoLoginEnabled)
            {
                progress("Logging in…");
                await _login(pid, profile, cancellationToken);
                progress(template?.ProxyEnabled == true ? "Подтверждение HWID и прокси после входа персонажа…" : "Подтверждение HWID после входа персонажа…");
                await _processes.ValidateProtectionAsync(pid, true, cancellationToken);
            }
            progress(profile.AutoLoginEnabled ? "Character selected" : "Client ready · log in manually");
            return session;
        }
        catch (Exception error)
        {
            if (session is not null) Stop(profile.Id);
            var protection = Protection(profile.Id);
            _lastProtection[profile.Id] = protection with { Error = protection.Error ?? error.GetBaseException().Message };
            throw;
        }
        finally { _launching.Remove(profile.Id); }
    }

    public bool Owns(Guid profileId, ClientSession session) =>
        _owned.TryGetValue(profileId, out var owned) && owned == session;

    private async Task<int> LaunchProcessWithRetryAsync(CollectorProfile profile,LaunchTemplate? template,
        Action<string> progress,CancellationToken token)
    {
        for(var attempt=1;;attempt++)
        {
            try { return await _processes.LaunchAndBindAsync(profile,template,token); }
            catch(ClientStartupException e) when(attempt<3)
            {
                progress($"{e.Message} Retrying launch {attempt+1}/3 in {attempt*15} seconds…");
                await Task.Delay(TimeSpan.FromSeconds(attempt*15),token);
            }
        }
    }

    public void Stop(Guid profileId)
    {
        if (!_owned.Remove(profileId, out var session)) return;
        var protection = _processes.Protection(session.ProcessId);
        _lastProtection[profileId] = protection.Failed ? protection : ClientProtectionStatus.Pending with { ProxyRequired = protection.ProxyRequired };
        if (_identity(session.ProcessId) == session) _processes.Terminate(session.ProcessId);
        else _processes.Release(session.ProcessId);
    }

    public async Task StopAndWaitAsync(Guid profileId,ClientSession session,CancellationToken cancellationToken)
    {
        if(!Owns(profileId,session)) throw new InvalidOperationException("This profile does not own the client to be replaced.");
        Stop(profileId);
        var deadline=DateTimeOffset.UtcNow.AddSeconds(10);
        while(_identity(session.ProcessId)==session)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(DateTimeOffset.UtcNow>=deadline) throw new TimeoutException("The previous client has not exited; character change was stopped.");
            await Task.Delay(100,cancellationToken);
        }
        await _restartPause(cancellationToken);
    }

    public void ReleaseExited(Guid profileId)
    {
        if (_owned.TryGetValue(profileId, out var session) && _identity(session.ProcessId) != session)
        {
            var protection = _processes.Protection(session.ProcessId);
            _lastProtection[profileId] = protection.Failed ? protection : ClientProtectionStatus.Pending with { ProxyRequired = protection.ProxyRequired };
            _owned.Remove(profileId);
            _processes.Release(session.ProcessId);
        }
    }

    public void StopOwnedClients()
    {
        foreach (var profileId in _owned.Keys.ToArray()) Stop(profileId);
    }
}
