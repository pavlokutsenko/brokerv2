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
    private readonly Dictionary<Guid, ClientSession> _owned = [];
    private readonly HashSet<Guid> _launching = [];

    public LaunchModule(IClientProcessService? processes = null,
        Func<int, CollectorProfile, CancellationToken, Task>? login = null,
        Func<int, ClientSession?>? identity = null)
    {
        _processes = processes ?? new ClientProcessService();
        _login = login ?? new ClientLoginService().EnterAsync;
        _identity = identity ?? ClientProcessIdentity.Read;
    }

    public async Task<ClientSession> LaunchAsync(CollectorProfile profile, LaunchTemplate? template,
        Action<string> progress, CancellationToken cancellationToken)
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
            var pid = await _processes.LaunchAndBindAsync(profile, template, cancellationToken);
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

    public void Stop(Guid profileId)
    {
        if (!_owned.Remove(profileId, out var session)) return;
        if (_identity(session.ProcessId) == session) _processes.Terminate(session.ProcessId);
        else _processes.Release(session.ProcessId);
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
