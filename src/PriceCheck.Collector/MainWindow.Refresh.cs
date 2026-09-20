using System.Net.Http;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async Task RefreshSelectedAsync()
    {
        if (SelectedRuntime is null) return;
        await RefreshRuntimesAsync([SelectedRuntime]);
    }

    private Task RefreshAllAsync() => RefreshRuntimesAsync(Runtimes.ToArray());

    private async Task RefreshRuntimesAsync(IReadOnlyList<Models.ProfileRuntime> runtimes)
    {
        if (_refreshing || runtimes.Count == 0) return;
        _refreshing = true;
        try
        {
            foreach (var runtime in runtimes)
                await RefreshRuntimeAsync(runtime);
        }
        finally { _refreshing = false; }
    }

    private async Task RefreshRuntimeAsync(Models.ProfileRuntime runtime)
    {
        try
        {
            if (runtime.ProcessId is int pid && !_processes.IsAlive(pid))
            {
                await _radarSessions.StopAsync(pid);
                _preparedPricePids.Remove(pid);
                _processes.Release(pid);
                runtime.ProcessId = null;
                runtime.IsCollectionEnabled = false;
                StopBrokerSchedule(runtime);
                runtime.Profile.LastProcessId = null;
                runtime.Status = "Клиент завершён";
                await SaveProfilesAsync();
            }

            var radar = runtime.ProcessId is int livePid
                ? _radarSessions.Snapshot(livePid, GetCenterZone(runtime.Profile), runtime.IsCollectionEnabled)
                : null;
            if (radar is not null) runtime.Radar = radar;
            if (radar is not null && runtime.IsCollectionEnabled && runtime.Profile.Role == Models.CollectorRole.BrokerRadar && radar.IsInsideCenterZone)
            {
                await UploadRadarWhenChangedAsync(runtime, radar);
                TryStartBrokerCycle(runtime, radar);
            }
            else if (radar is not null && runtime.IsCollectionEnabled && runtime.Profile.Role == Models.CollectorRole.PriceVerifier)
            {
                TryStartPriceWorker(runtime, radar);
            }
            // Broker values must belong to this profile's live session. Never
            // surface old research JSON as if it were current market state.
            runtime.Broker = null;
        }
        catch (IOException) { }
        catch (JsonException) { }
        catch (Exception exception)
        {
            if (runtime.ProcessId is int pid)
            {
                await _radarSessions.StopAsync(pid);
                runtime.Radar = null;
                runtime.IsCollectionEnabled = false;
                runtime.Status = $"Радар остановлен, клиент оставлен: {exception.GetBaseException().Message}";
                Log(runtime.Status);
                await SaveProfilesAsync();
            }
        }
    }

    private async Task UploadRadarWhenChangedAsync(Models.ProfileRuntime runtime, Models.RadarSnapshot radar)
    {
        var visible = radar.Traders.Where(x => x.IsVisible).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var fingerprint = string.Join('|', visible.Select(x => $"{x.Name.ToUpperInvariant()}:{x.ObjectId}:{x.KioskType}:{x.X:F0}:{x.Y:F0}"));
        var now = DateTimeOffset.UtcNow;
        var unchanged = _lastUploadedRadarFingerprints.TryGetValue(runtime.Profile.Id, out var previous) && previous == fingerprint;
        var recentlyUploaded = _lastRadarUploads.TryGetValue(runtime.Profile.Id, out var last) && now - last < TimeSpan.FromSeconds(10);
        if (unchanged && recentlyUploaded) return;
        try
        {
            await _marketApi.UploadRadarAsync(runtime.Profile, radar);
            _lastUploadedRadarFingerprints[runtime.Profile.Id] = fingerprint;
            _lastRadarUploads[runtime.Profile.Id] = now;
        }
        catch (HttpRequestException exception)
        {
            if (!_lastRadarUploads.TryGetValue(runtime.Profile.Id, out var failedAt) || now - failedAt >= TimeSpan.FromSeconds(30))
            {
                _lastRadarUploads[runtime.Profile.Id] = now;
                Log($"{runtime.Profile.Name}: сервер недоступен — {exception.Message}");
            }
        }
    }
}
