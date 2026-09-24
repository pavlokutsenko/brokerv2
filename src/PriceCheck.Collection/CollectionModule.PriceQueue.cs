using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private void TryStartPriceWorker(ProfileRuntime runtime, RadarSnapshot radar)
    {
        if (runtime.Profile.Role != CollectorRole.BrokerRadar || !runtime.IsCollectionEnabled ||
            runtime.ProcessId is not int || _priceWorkerProfiles.Contains(runtime.Profile.Id) ||
            _brokerRunningProfiles.Contains(runtime.Profile.Id)) return;
        if (_nextPriceClaims.TryGetValue(runtime.Profile.Id, out var due) && due > DateTimeOffset.UtcNow) return;
        if (!_nextBrokerRuns.TryGetValue(runtime.Profile.Id, out var brokerAt))
        {
            brokerAt = DateTimeOffset.UtcNow.AddMinutes(
                Math.Clamp(runtime.Profile.BrokerIntervalMinutes, 1, 1440));
            _nextBrokerRuns[runtime.Profile.Id] = brokerAt;
        }
        var brokerDue = brokerAt <= DateTimeOffset.UtcNow;
        if (brokerDue && radar.IsInsideCenterZone) return;
        _priceWorkerProfiles.Add(runtime.Profile.Id);
        TrackJob(runtime, brokerDue ? ReturnToCenterAsync(runtime) : RunLocalPriceWorkerAsync(runtime, radar));
    }

    private async Task RunLocalPriceWorkerAsync(ProfileRuntime runtime, RadarSnapshot radar)
    {
        IReadOnlyList<LocalPriceTarget> targets = [];
        try
        {
            if (runtime.ProcessId is not int pid || !runtime.IsCollectionEnabled) return;
            await EnsurePriceSessionAsync(runtime, pid);
            if (!runtime.IsCollectionEnabled || runtime.ProcessId != pid) return;
            var plan = _localPriceQueue.PlanNext(runtime.Profile.Id, radar, 95, 64);
            if (plan is null)
            {
                runtime.Status = "Local price queue is empty";
                _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddSeconds(2);
                return;
            }
            targets = plan.Batch.Count > 0 ? plan.Batch : [plan.Anchor];
            if (plan.Batch.Count > 0) await RunLocalPriceBatchAsync(runtime, pid, plan.Batch);
            else await RunTravelToAnchorAsync(runtime, pid, plan.Anchor);
            _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow;
        }
        catch (Exception exception)
        {
                runtime.Status = "Shop read failed · retrying later";
                Log($"{runtime.Profile.Name}: price collection — {exception.GetBaseException().Message.Trim()}");
            foreach (var target in targets) _localPriceQueue.Fail(runtime.Profile.Id, target.TraderKey);
            _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddSeconds(2);
        }
        finally
        {
            _priceWorkerProfiles.Remove(runtime.Profile.Id);
            if (!runtime.IsCollectionEnabled) await CleanupPriceSessionAsync(runtime);
        }
    }

    private async Task RunLocalPriceBatchAsync(ProfileRuntime runtime, int pid, IReadOnlyList<LocalPriceTarget> batch)
    {
            runtime.Status = $"Reading nearby shops: {batch.Count}";
        var folder = Path.Combine(AppContext.BaseDirectory, "price-snapshots"); Directory.CreateDirectory(folder);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var input = Path.Combine(folder, $"{runtime.Profile.Name}-local-{stamp}.input.json");
        var output = Path.Combine(folder, $"{runtime.Profile.Name}-local-{stamp}.json");
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(batch.Select(item => new
        {
            object_id = item.ObjectId, kiosk_type = item.KioskType, name = item.TraderName, x = item.X, y = item.Y
        })));
            await RunWorkerAsync(pid, "price-batch", output, start => { start.ArgumentList.Add("--input"); start.ArgumentList.Add(input); });
        if (!runtime.IsCollectionEnabled) return;
            var capture = JsonSerializer.Deserialize<PriceBatchCaptureFile>(await File.ReadAllTextAsync(output)) ?? throw new InvalidDataException("Batch Worker returned an empty snapshot");
        var shops = capture.Shops.ToDictionary(item => item.Trader.ObjectId);
        foreach (var target in batch)
        {
            if (shops.TryGetValue(target.ObjectId, out var shop) && shop.Rows.Count > 0)
            {
                await _uploadOutbox.EnqueueLocalPriceResultAsync(runtime.Profile, target, new ShopCaptureFile { Side = shop.Side, Rows = shop.Rows });
                _localPriceQueue.Complete(runtime.Profile.Id, target.TraderKey);
            }
            else _localPriceQueue.Fail(runtime.Profile.Id, target.TraderKey);
        }
            runtime.Status = $"Prices: {capture.Shops.Count}/{batch.Count} · {capture.ShopsPerSecond:F1} shops/s";
            Log($"{runtime.Profile.Name}: local batch {capture.Shops.Count}/{batch.Count} in {capture.ElapsedSeconds:F3} sec ({capture.ShopsPerSecond:F1}/s)");
    }

    private async Task RunTravelToAnchorAsync(ProfileRuntime runtime, int pid, LocalPriceTarget target)
    {
            runtime.Status = $"Moving to {target.TraderName} · attempt {target.AttemptCount + 1}";
        var folder = Path.Combine(AppContext.BaseDirectory, "movement"); Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, $"{runtime.Profile.Name}-{target.ObjectId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await RunWorkerAsync(pid, "move", output, start =>
        {
            start.ArgumentList.Add("--target-x"); start.ArgumentList.Add(target.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--target-y"); start.ArgumentList.Add(target.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--radius"); start.ArgumentList.Add("8");
        });
            runtime.Status = $"At {target.TraderName}'s sector · reading shops";
            Log($"{runtime.Profile.Name}: reached sector near {target.TraderName}");
    }

    private async Task ReturnToCenterAsync(ProfileRuntime runtime)
    {
        try
        {
            if (runtime.ProcessId is not int pid || GetCenterZone(runtime.Profile) is not MarketZone center) return;
            await EnsurePriceSessionAsync(runtime, pid);
            if (!runtime.IsCollectionEnabled) return;
            runtime.Status = "Returning to center to refresh quantities…";
            var folder = Path.Combine(AppContext.BaseDirectory, "movement"); Directory.CreateDirectory(folder);
            var output = Path.Combine(folder, $"{runtime.Profile.Name}-center-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await RunWorkerAsync(pid, "move", output, start =>
            {
                start.ArgumentList.Add("--target-x"); start.ArgumentList.Add(center.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
                start.ArgumentList.Add("--target-y"); start.ArgumentList.Add(center.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
                start.ArgumentList.Add("--radius"); start.ArgumentList.Add("180");
            });
                runtime.Status = "At center · refreshing quantities with broker";
            _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow;
        }
        catch (Exception exception)
        {
                runtime.Status = "Could not return to center · retrying later";
                Log($"{runtime.Profile.Name}: return to center — {exception.GetBaseException().Message.Trim()}");
            _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddSeconds(5);
        }
        finally { _priceWorkerProfiles.Remove(runtime.Profile.Id); }
    }

    private async Task RunWorkerAsync(int pid, string mode, string output, Action<ProcessStartInfo> configure)
    {
        var session = _attached.Values.SingleOrDefault(value => value.ProcessId == pid)
            ?? throw new InvalidOperationException("Reader is disconnected.");
        await _worker.RunAsync(session, mode, output, configure);
    }
    private async Task EnsurePriceSessionAsync(ProfileRuntime runtime, int pid)
    {
        if (!runtime.IsCollectionEnabled) throw new OperationCanceledException("Collection stopped.");
        if (_preparedPricePids.Contains(pid)) return;
            runtime.Status = "Preparing collector…";
        await RunWorkerAsync(pid, "price-prepare", "NUL", _ => { });
        _preparedPricePids.Add(pid);
            Log($"{runtime.Profile.Name}: price session PID {pid} ready");
    }

    private async Task CleanupPriceSessionAsync(ProfileRuntime runtime)
    {
        if (_priceWorkerProfiles.Contains(runtime.Profile.Id) || runtime.ProcessId is not int pid || !_preparedPricePids.Contains(pid)) return;
        if (runtime.Session is null || !_isCurrent(runtime.Session)) { _preparedPricePids.Remove(pid); return; }
        await RunWorkerAsync(pid, "cleanup", "NUL", _ => { });
        _preparedPricePids.Remove(pid);
    }
}