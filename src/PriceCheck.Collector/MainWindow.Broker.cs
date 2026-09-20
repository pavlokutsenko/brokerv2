using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class MainWindow
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
        _ = RunBrokerCycleAsync(runtime, radar);
    }

    private async Task RunBrokerCycleAsync(ProfileRuntime runtime, RadarSnapshot radar)
    {
        try
        {
            await _brokerCycleGate.WaitAsync();
            if (!runtime.IsCollectionEnabled || runtime.ProcessId is not int pid) return;
            await CleanupPriceSessionAsync(runtime);
            runtime.Status = "Брокер: получаю все лавки…";
            Log($"{runtime.Profile.Name}: брокерный проход запущен");
            var worker = BrokerRuntimeIsolation.WorkerFor(pid);
            var outputFolder = Path.Combine(AppContext.BaseDirectory, "broker-snapshots");
            Directory.CreateDirectory(outputFolder);
            var output = Path.Combine(outputFolder, $"{runtime.Profile.Name}-{runtime.Profile.City}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            var start = new ProcessStartInfo
            {
                FileName = worker,
                WorkingDirectory = Path.GetDirectoryName(worker)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("--pid"); start.ArgumentList.Add(pid.ToString());
            start.ArgumentList.Add("--output"); start.ArgumentList.Add(output);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить брокерный проход");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(); var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(); var stdout = await stdoutTask; var stderr = await stderrTask;
            if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            var inventory = JsonSerializer.Deserialize<BrokerInventoryFile>(await File.ReadAllTextAsync(output)) ??
                throw new InvalidDataException("Пустой результат брокера");
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
                runtime.Status = "Радар и брокер работают";
                _nextBrokerRuns[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddMinutes(
                    Math.Clamp(runtime.Profile.BrokerIntervalMinutes, 1, 1440));
                Log($"{runtime.Profile.Name}: брокер поставлен на отправку — {inventory.Summary.UniqueTraders:N0} трейдеров, {inventory.Summary.ListingRows:N0} строк");
            }
        }
        catch (Exception exception)
        {
            runtime.Status = "Радар работает · ошибка брокера";
            Log($"{runtime.Profile.Name}: брокер — {exception.GetBaseException().Message.Trim()}");
            _nextBrokerRuns[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddSeconds(30);
        }
        finally
        {
            _brokerRunningProfiles.Remove(runtime.Profile.Id);
            if (_brokerCycleGate.CurrentCount == 0) _brokerCycleGate.Release();
        }
    }

    private void StopBrokerSchedule(ProfileRuntime runtime)
    {
        _nextBrokerRuns.Remove(runtime.Profile.Id);
    }
}
