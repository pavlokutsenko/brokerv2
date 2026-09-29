using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    public async Task RefreshAsync(ProfileRuntime runtime)
    {
        try
        {
            if (!_attached.TryGetValue(runtime.Profile.Id, out var session)) return;
            if (!_isCurrent(session))
            {
                runtime.ClientFault="Client exited or changed";
                await DetachAsync(runtime);
                runtime.Status = "Reader disconnected · client exited or changed";
                return;
            }

            var radar = runtime.ProcessId is int livePid
                ? _radarSessions.Snapshot(livePid, GetCenterZone(runtime.Profile), runtime.IsCollectionEnabled)
                : null;
            if (radar is not null)
            {
                runtime.Radar = radar; ObserveLocalState(runtime,radar);
                if(_localStores.TryGetValue(runtime.Profile.Id,out var store))
                {
                    runtime.UploadStatus=$"Local outbox · {store.PendingUploads} individual operations pending";
                    runtime.BrokerDelivery=store.LatestBrokerDelivery;
                    runtime.Cycle=store.Status(runtime.Cycle);
                }
            }
            if (radar is not null && runtime.IsCollectionEnabled)
            {
                TickCycle(runtime,radar);
            }
            await PublishCycleStatusAsync(runtime);
            // Broker values must belong to this profile's live session. Never
            // surface old research JSON as if it were current market state.
        }
        catch (IOException) { }
        catch (JsonException) { }
        catch (Exception exception)
        {
            var faultFolder=Path.Combine(CycleQueue.Root,runtime.Profile.Id.ToString("N"),"runs");
            try
            {
                Directory.CreateDirectory(faultFolder);
                var faultPath=Path.Combine(faultFolder,$"reader-error-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
                File.WriteAllText(faultPath,exception.ToString());
                Log($"{runtime.Profile.Name}: reader fault details: {faultPath}");
            }
            catch(Exception io) when(io is IOException or UnauthorizedAccessException) { }
            runtime.ClientFault=$"Reader failed: {exception.GetBaseException().Message}";
            if (runtime.ProcessId is int pid)
            {
                await DetachAsync(runtime);
                runtime.Radar = null;
                runtime.IsCollectionEnabled = false;
                runtime.Status = $"Radar stopped; client remains open: {exception.GetBaseException().Message}";
                Log(runtime.Status);

            }
        }
    }

    private async Task UploadRadarWhenChangedAsync(ProfileRuntime runtime, RadarSnapshot radar)
    {
        var visible = radar.Traders.Where(x => x.IsVisible).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        // A receive-hook can briefly expose an empty store while it is being
        // recovered. An empty authoritative snapshot would incorrectly mark
        // the whole market as inactive on the server. A real market shutdown
        // is handled by individual delete packets after the store is warm.
        if (visible.Length == 0) return;
        var fingerprint = string.Join('|', visible.Select(x => $"{x.Name.ToUpperInvariant()}:{x.ObjectId}:{x.KioskType}:{x.X:F0}:{x.Y:F0}"));
        var now = DateTimeOffset.UtcNow;
        var unchanged = _lastUploadedRadarFingerprints.TryGetValue(runtime.Profile.Id, out var previous) && previous == fingerprint;
        var recentlyUploaded = _lastRadarUploads.TryGetValue(runtime.Profile.Id, out var last) && now - last < TimeSpan.FromSeconds(10);
        if (unchanged && recentlyUploaded) return;
        try
        {
            await _uploadOutbox.EnqueueRadarAsync(runtime.Profile, radar);
            _lastUploadedRadarFingerprints[runtime.Profile.Id] = fingerprint;
            _lastRadarUploads[runtime.Profile.Id] = now;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!_lastRadarUploads.TryGetValue(runtime.Profile.Id, out var failedAt) || now - failedAt >= TimeSpan.FromSeconds(30))
            {
                _lastRadarUploads[runtime.Profile.Id] = now;
                Log($"{runtime.Profile.Name}: upload queue unavailable — {exception.Message}");
            }
        }
    }
}
