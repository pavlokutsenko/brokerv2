using PriceCheck.Contracts;
using PriceCheck.Windows;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Runtime.Radar;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

// Reader lifetime has no access to launch, identity replacement, proxy or game termination.
// Calls are serialized by the host's UI context; worker operations remain asynchronous.
public sealed partial class CollectionModule
{
    private readonly IRadarSessions _radarSessions;
    private readonly Func<ClientSession, bool> _isCurrent;
    private readonly ICollectionWorker _worker;
    private readonly Action _startUploadWorker;
    private readonly Dictionary<Guid, ClientSession> _attached = [];
    private readonly Dictionary<int, Guid> _owners = [];
    private readonly Dictionary<Guid, Task> _activeJobs = [];
    private readonly HashSet<Guid> _attaching = [];
    private readonly HashSet<Guid> _transitions = [];
    private readonly Dictionary<string,Guid> _marketOwners=[];
    private readonly ServerUploadOutbox _uploadOutbox;
    private readonly IMarketTaskClient _marketTasks;
    private readonly LocalPriceQueue _localPriceQueue = new();
    private readonly Dictionary<Guid, string> _lastUploadedRadarFingerprints = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastRadarUploads = [];
    private readonly SemaphoreSlim _brokerCycleGate = new(1, 1);
    private readonly HashSet<Guid> _brokerRunningProfiles = [];
    private readonly Dictionary<Guid, DateTimeOffset> _nextBrokerRuns = [];
    private readonly HashSet<Guid> _priceWorkerProfiles = [];
    private readonly HashSet<int> _preparedPricePids = [];
    private readonly Dictionary<Guid, DateTimeOffset> _nextPriceClaims = [];
    public event Action<string>? Message;

    public CollectionModule(IRadarSessions? radarSessions = null, Func<ClientSession, bool>? isCurrent = null,
        ICollectionWorker? worker = null, Action? startUploadWorker = null, ServerUploadOutbox? uploadOutbox = null,
        IMarketTaskClient? marketTasks = null)
    {
        _radarSessions = radarSessions ?? new RadarSessionManager();
        _isCurrent = isCurrent ?? ClientProcessIdentity.IsCurrent;
        _worker = worker ?? new CollectionWorker(_isCurrent);
        _startUploadWorker = startUploadWorker ?? UploadWorkerProcess.EnsureRunning;
        _uploadOutbox = uploadOutbox ?? new(_startUploadWorker);
        _marketTasks = marketTasks ?? new MarketTaskClient();
    }

    public async Task AttachAsync(ProfileRuntime runtime, CancellationToken cancellationToken)
    {
        var session = runtime.Session ?? throw new InvalidOperationException("Select a running client first.");
        var id = runtime.Profile.Id;
        if (_transitions.Contains(id)) throw new InvalidOperationException("Reader operation is still in progress.");
        if (_attached.TryGetValue(id, out var existing))
        {
            if (existing == session) return;
            throw new InvalidOperationException("Disconnect the previous reader first.");
        }
        if (!_isCurrent(session)) throw new InvalidOperationException("Client exited or PID was reused. Refresh the process list.");
        if (_owners.ContainsKey(session.ProcessId) || !_attaching.Add(id))
            throw new InvalidOperationException("This client or profile already has a reader.");
        if(_attached.Keys.Any(other=>other!=id && _localStoreMarkets.GetValueOrDefault(other)==CycleQueue.Key(runtime.Profile.Name)))
        {_attaching.Remove(id);throw new InvalidOperationException("This market already has a connected collector reader.");}
        _owners.Add(session.ProcessId, id);
        try
        {
            await _radarSessions.StartAsync(session.ProcessId, GetCenterZone(runtime.Profile), false, cancellationToken);
            if (!_isCurrent(session)) throw new InvalidOperationException("Client changed while attaching.");
            _attached.Add(id, session);
            runtime.ReaderAttached = true;
            runtime.ClientFault = null;
            runtime.IsCollectionEnabled = false;
            runtime.Profile.CollectionEnabled = false;
            runtime.Status = "Reader connected · collection stopped";
            Log($"{runtime.Profile.Name}: reader connected to PID {session.ProcessId}");
        }
        catch
        {
            await _radarSessions.StopAsync(session.ProcessId);
            _owners.Remove(session.ProcessId);
            throw;
        }
        finally { _attaching.Remove(id); }
    }

    public async Task SetCollectionAsync(ProfileRuntime runtime, bool enabled)
    {
        if (!_transitions.Add(runtime.Profile.Id)) throw new InvalidOperationException("Reader operation is still in progress.");
        try
        {
            if(!enabled && _cycles.TryGetValue(runtime.Profile.Id,out var stopped)) stopped.ContinueAfterClientChange=false;
            await SetCollectionCoreAsync(runtime, enabled);
        }
        finally { _transitions.Remove(runtime.Profile.Id); }
    }

    private async Task SetCollectionCoreAsync(ProfileRuntime runtime, bool enabled)
    {
        var wasEnabled=runtime.IsCollectionEnabled;
        if(enabled&&wasEnabled)return;
        if (enabled)
        {
            var market=CycleQueue.Key(runtime.Profile.Name);
            if(_marketOwners.TryGetValue(market,out var owner)&&owner!=runtime.Profile.Id)
                throw new InvalidOperationException("Only one active collector may own a market.");
            if (!_attached.TryGetValue(runtime.Profile.Id, out var session) || !_isCurrent(session))
                throw new InvalidOperationException("Connect the reader to a running client first.");
            if (GetCenterZone(runtime.Profile) is null) throw new InvalidOperationException("Сохранённый центр Гирана отсутствует или неоднозначен. Проверьте восстановленную конфигурацию.");
            if (!Uri.TryCreate(runtime.Profile.ServerUrl, UriKind.Absolute, out var server) || server.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Enter a valid API server address.");
            _startUploadWorker();
            if (!ResumeCycleAfterClientChange(runtime)) StartCycle(runtime);
            runtime.IsCollectionEnabled = runtime.Profile.CollectionEnabled = true;
            _marketOwners[market]=runtime.Profile.Id;
            _nextBrokerRuns[runtime.Profile.Id] = DateTimeOffset.MinValue;
            runtime.Status = "Collection started";
            Log($"INFO {runtime.Profile.Name}: collection started · PID {runtime.ProcessId}");
        }
        else
        {
            if(_cycles.TryGetValue(runtime.Profile.Id,out var stopping) &&
                stopping.Phase is not "Stopped" and not "Waiting for server") stopping.ResumePhase=stopping.Phase;
            runtime.IsCollectionEnabled = runtime.Profile.CollectionEnabled = false;
            RequestCycleStop(runtime);
            StopBrokerSchedule(runtime);
            runtime.Status = "Finishing current reader operation…";
            if(wasEnabled)Log($"INFO {runtime.Profile.Name}: collection stop requested · PID {runtime.ProcessId} · finishing current operation");
            // Let an in-flight native call complete and restore its patches. A timeout
            // leaves the reader attached so a later Stop can finish cleanup safely.
            if (_activeJobs.TryGetValue(runtime.Profile.Id, out var job))
                await job.WaitAsync(TimeSpan.FromMinutes(2));
            await CleanupPriceSessionAsync(runtime);
            foreach(var key in _marketOwners.Where(pair=>pair.Value==runtime.Profile.Id).Select(pair=>pair.Key).ToArray())_marketOwners.Remove(key);
            runtime.Status = runtime.ReaderAttached ? "Reader connected · collection stopped" : "Reader disconnected";
            runtime.Cycle = runtime.Cycle with { Phase="Stopped", Detail=runtime.Status };
            if(wasEnabled)Log($"INFO {runtime.Profile.Name}: collection stopped · PID {runtime.ProcessId}");
        }
    }

    public async Task DetachAsync(ProfileRuntime runtime,bool preserveCycle=false)
    {
        if (!_transitions.Add(runtime.Profile.Id)) throw new InvalidOperationException("Reader operation is still in progress.");
        try
        {
            if(!preserveCycle && _cycles.TryGetValue(runtime.Profile.Id,out var stopped)) stopped.ContinueAfterClientChange=false;
            await DetachCoreAsync(runtime);
        }
        finally { _transitions.Remove(runtime.Profile.Id); }
    }

    private async Task DetachCoreAsync(ProfileRuntime runtime)
    {
        if (_attaching.Contains(runtime.Profile.Id)) throw new InvalidOperationException("Reader is still connecting.");
        await SetCollectionCoreAsync(runtime, false);
        if (_attached.TryGetValue(runtime.Profile.Id, out var session))
        {
            await _radarSessions.StopAsync(session.ProcessId);
            _attached.Remove(runtime.Profile.Id);
            _owners.Remove(session.ProcessId);
        }
        _lastUploadedRadarFingerprints.Remove(runtime.Profile.Id);
        _lastRadarUploads.Remove(runtime.Profile.Id);
        _nextPriceClaims.Remove(runtime.Profile.Id);
        _activeJobs.Remove(runtime.Profile.Id);
        _localPriceQueue.Clear(runtime.Profile.Id);
        runtime.ReaderAttached = false;
        runtime.Radar = null;
        runtime.Broker = null;
        runtime.Status = "Reader disconnected · client remains open";
    }

    private void TrackJob(ProfileRuntime runtime, Task task) => _activeJobs[runtime.Profile.Id] = task;
    private void Log(string value) => Message?.Invoke(value);
    public static MarketZone? GetCenterZone(CollectorProfile profile) =>
        SavedGiranCenter.Resolve(profile) is { } center ? new(center.X, center.Y, 500) : null;
}
