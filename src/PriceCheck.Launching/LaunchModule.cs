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
        try
        {
            if (_owned.TryGetValue(profile.Id, out var current))
            {
                if (_identity(current.ProcessId) == current) return current;
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
            if(beforeLogin is not null) await beforeLogin(session,cancellationToken);
            if (profile.AutoLoginEnabled)
            {
                progress("Logging in…");
                await _login(pid, profile, cancellationToken);
            }
            progress(profile.AutoLoginEnabled ? "Character selected" : "Client ready · log in manually");
            return session;
        }
        catch
        {
            if (session is not null) Stop(profile.Id);
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
            _owned.Remove(profileId);
            _processes.Release(session.ProcessId);
        }
    }

    public void StopOwnedClients()
    {
        foreach (var profileId in _owned.Keys.ToArray()) Stop(profileId);
    }
}
