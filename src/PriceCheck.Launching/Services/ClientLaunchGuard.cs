using PriceCheck.Contracts;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Collector.Services;

internal sealed class ClientLaunchGuard : IDisposable
{
    private readonly LaunchGuardMapping _mapping;
    private readonly ProxyTcpBroker _broker;
    private readonly CancellationTokenSource _stop = new();
    private readonly Action _terminate;
    private readonly Func<int,bool> _alive;
    private readonly int _root;
    private int _pid;
    private readonly object _gate = new();
    private ClientProtectionStatus _status = ClientProtectionStatus.Pending;
    public ClientProtectionStatus Status { get { lock (_gate) return _status; } }
    public ClientLaunchGuard(LaunchGuardMapping mapping, ProxyTcpBroker broker, int root,
        Func<int,bool> alive, Action terminate)
    {
        _mapping = mapping; _broker = broker; _root = root; _alive = alive; _terminate = terminate;
        _status = ClientProtectionStatus.Pending with { ProxyRequired = broker.ProxyEnabled };
        CheckRoute(root); mapping.ControllerReady(true);
        _ = Task.Run(RunAsync);
    }
    public void Bind(int pid) { _mapping.Bind(pid); Volatile.Write(ref _pid, pid); }
    public bool IsAgentReady(PriceCheck.Contracts.ClientSession session)
    {
        var status = Status;
        return Volatile.Read(ref _pid) == session.ProcessId && status.HardwareReady &&
            !status.Failed && _mapping.IsAgentReady(session);
    }
    public async Task RequireAsync(bool world, CancellationToken token)
    {
        var deadline = Environment.TickCount64 + (world ? LaunchTimeouts.WorldReadyMilliseconds : LaunchTimeouts.AgentReadyMilliseconds);
        for (;;)
        {
            token.ThrowIfCancellationRequested();
            var status = Status;
            if (status.Failed) throw new LaunchProtectionException(status.Error!);
            if (status.HardwareReady && (!status.ProxyRequired || status.ProxyReady) && (!world || status.WorldIdentityApplied)) return;
            if (Environment.TickCount64 >= deadline)
            {
                Fail(world ? "HWID: применение идентичности и обмен данными мира не подтверждены."
                    : "HWID: подмена идентичности не прошла проверку.");
                throw new LaunchProtectionException(Status.Error!);
            }
            await Task.Delay(100, token);
        }
    }
    private ulong CheckRoute(int pid)
    {
        using var device = new Lu4Device();
        var route = device.QueryProxyGuard(pid);
        if (route.Capabilities != 31 || !route.Active || route.HostProcessId != Environment.ProcessId ||
            route.ListenerPort != _broker.ListenerPort)
            throw new LaunchProtectionException(_broker.ProxyEnabled ? "Прокси: драйвер не подтвердил запрет прямого трафика для процесса."
                : "HWID: драйвер не подтвердил маршрут проверки игрового процесса.");
        // A denied attempt proves that the policy enforced the restriction.
        // Unsupported sockets must stay denied, but normal probing/fallback by
        // the client is not a lost protection lease or a direct traffic leak.
        return route.BlockedConnections;
    }
    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                _mapping.Heartbeat();
                var pid = Volatile.Read(ref _pid);
                var state = _mapping.Read();
                if (state.Error != 0) throw new LaunchProtectionException(NativeError(state.Error));
                if (_broker.Error is { } error) throw new LaunchProtectionException(error);
                // A natural client exit belongs to recovery. A reported native
                // failure above remains fatal and cannot trigger a relaunch.
                if (pid != 0 && !_alive(pid))
                {
                    lock (_gate) _status = ClientProtectionStatus.Pending with { ProxyRequired = _broker.ProxyEnabled };
                    _broker.AllowLogin = false;
                    return;
                }
                var blocked = _alive(_root) ? CheckRoute(_root) : 0UL;
                if (pid != 0 && _alive(pid)) blocked = Math.Max(blocked, CheckRoute(pid));
                if (state.Hardware && Environment.TickCount64 - state.Tick > LaunchTimeouts.HeartbeatMilliseconds)
                    throw new LaunchProtectionException("HWID: агент перестал подтверждать подмену.");
                // The driver route and actual CONNECT/pumps are checked continuously.
                // A separate idle CONNECT can time out or hit provider limits while
                // existing game tunnels are healthy; it must not revoke their lease.
                // send() can finish into the local TCP buffer before HTTP CONNECT.
                // Match each native HWID handshake to its relay generation;
                // an old world's confirmation cannot authorize a new connection.
                var applied = state.World && state.WorldCount == _broker.WorldConnections && _broker.WorldOpenedAt is not null;
                if (_broker.WorldOpenedAt is long started && Environment.TickCount64 - started > LaunchTimeouts.WorldReadyMilliseconds &&
                    !(applied && _broker.WorldTrafficConfirmed))
                    throw new LaunchProtectionException("HWID: подмена и обмен данными текущего мира не подтверждены.");
                _broker.AllowLogin = state.Hardware;
                lock (_gate) _status = new(state.Hardware, applied && _broker.WorldTrafficConfirmed,
                    _broker.ProxyEnabled, _broker.Connections, _broker.SentBytes, _broker.ReceivedBytes, _mapping.IdentityTag, null, blocked, _broker.ProxyEnabled);
                await Task.Delay(500, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception) when (!_stop.IsCancellationRequested) { Fail(exception.GetBaseException().Message); }
        finally
        {
            _stop.Cancel();
            _mapping.Dispose();
            _stop.Dispose();
        }
    }
    private static string NativeError(int code) => code switch
    {
        1 or 4 => "Защита HWID/прокси: подтверждение контроллера потеряно; клиент остановлен.",
        2 or 5 => "HWID: агент применяет другую идентичность, чем выбранный профиль.",
        3 => "HWID: не удалось запустить непрерывную проверку агента.",
        6 => "Передача остановлена: защита HWID/прокси не подтверждена.",
        7 => "HWID: подмена при входе в мир не прошла проверку.",
        8 => "HWID: ответы Windows API не соответствуют выбранному шаблону.",
        9 => "HWID: версия или данные игрового драйвера не прошли проверку.",
        _ => $"HWID: проверка агента завершилась ошибкой {code}."
    };
    private void Fail(string error)
    {
        lock (_gate)
        {
            if (_status.Failed || _stop.IsCancellationRequested) return;
            _status = _status with { Error = error, ProxyReady = false };
        }
        _mapping.ControllerReady(false);
        _broker.AllowLogin = false;
        _terminate();
    }
    public void Dispose()
    {
        _broker.AllowLogin = false;
        try { _mapping.ControllerReady(false); } catch (ObjectDisposedException) { }
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
    }
}
