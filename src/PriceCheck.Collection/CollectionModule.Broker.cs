using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private void TryStartBrokerCycle(ProfileRuntime runtime, RadarSnapshot radar)
    {
        if (runtime.Profile.Role != CollectorRole.BrokerRadar || !runtime.IsCollectionEnabled ||
            !radar.IsInsideCenterZone || runtime.ProcessId is not int || _brokerRunningProfiles.Contains(runtime.Profile.Id) ||
            _priceWorkerProfiles.Contains(runtime.Profile.Id)) return;
        var now = DateTimeOffset.UtcNow;
        if (!_nextBrokerRuns.TryGetValue(runtime.Profile.Id, out var due))
        {
            _nextBrokerRuns[runtime.Profile.Id] = now.AddMinutes(
                Math.Clamp(runtime.Profile.BrokerIntervalMinutes, 1, 1440));
            return;
        }
        if (due > now) return;
        // Schedule the next pass after this one completes. A full broker scan
        // takes minutes; measuring the interval from its start starves the
        // price warm-up when the configured interval is short.
        _nextBrokerRuns[runtime.Profile.Id] = DateTimeOffset.MaxValue;
        _brokerRunningProfiles.Add(runtime.Profile.Id);
        TrackJob(runtime, RunBrokerCycleAsync(runtime, radar));
    }

    private async Task RunBrokerCycleAsync(ProfileRuntime runtime, RadarSnapshot radar)
    {
        var acquired = false;
        try
        {
            await _brokerCycleGate.WaitAsync();
            acquired = true;
            if (!runtime.IsCollectionEnabled || runtime.ProcessId is not int pid) return;
            await CleanupPriceSessionAsync(runtime);
            runtime.Status = "Broker: reading all shops…";
            Log($"{runtime.Profile.Name}: broker pass started");

            var outputFolder = Path.Combine(AppContext.BaseDirectory, "broker-snapshots");
            Directory.CreateDirectory(outputFolder);
            var output = Path.Combine(outputFolder, $"{runtime.Profile.Name}-{runtime.Profile.City}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            var session = runtime.Session ?? throw new InvalidOperationException("Reader disconnected.");
            await _worker.RunAsync(session, "broker", output, _ => { });
            if (!runtime.IsCollectionEnabled) return;
            var inventory = JsonSerializer.Deserialize<BrokerInventoryFile>(await File.ReadAllTextAsync(output)) ??
                    throw new InvalidDataException("Broker returned no data");
            if (runtime.IsCollectionEnabled)
            {
                await _uploadOutbox.EnqueueBrokerAsync(runtime.Profile, inventory);
                runtime.Broker = new BrokerSnapshot
                {
                    UniqueTraders = inventory.Summary.UniqueTraders,
                    ListingRows = inventory.Summary.ListingRows,
                    SellTraders = inventory.Rows.Where(x => x.StoreType == 1).Select(x => x.TraderObjectId).Distinct().Count(),
                    BuyTraders = inventory.Rows.Where(x => x.StoreType == 3).Select(x => x.TraderObjectId).Distinct().Count(),
                    PackageTraders = inventory.Rows.Where(x => x.StoreType == 8).Select(x => x.TraderObjectId).Distinct().Count(),
                    ElapsedSeconds = inventory.ElapsedSeconds,
                    CapturedAtUtc = inventory.CapturedAtUtc
                };
            runtime.Status = "Radar and broker active";
                _nextBrokerRuns[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddMinutes(
                    Math.Clamp(runtime.Profile.BrokerIntervalMinutes, 1, 1440));
            Log($"{runtime.Profile.Name}: broker queued for upload — {inventory.Summary.UniqueTraders:N0} traders, {inventory.Summary.ListingRows:N0} listings");
            }
        }
        catch (Exception exception)
        {
            runtime.Status = "Radar active · broker failed";
            Log($"{runtime.Profile.Name}: broker — {exception.GetBaseException().Message.Trim()}");
            _nextBrokerRuns[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddSeconds(30);
        }
        finally
        {
            _brokerRunningProfiles.Remove(runtime.Profile.Id);
            if (acquired) _brokerCycleGate.Release();
        }
    }

    private void StopBrokerSchedule(ProfileRuntime runtime)
    {
        _nextBrokerRuns.Remove(runtime.Profile.Id);
    }
}
