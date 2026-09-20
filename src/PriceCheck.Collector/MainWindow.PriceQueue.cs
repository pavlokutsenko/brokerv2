using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private void TryStartPriceWorker(ProfileRuntime runtime, RadarSnapshot radar)
    {
        if (runtime.Profile.Role != CollectorRole.BrokerRadar || !runtime.IsCollectionEnabled ||
            runtime.ProcessId is not int || _priceWorkerProfiles.Contains(runtime.Profile.Id) ||
            _brokerRunningProfiles.Contains(runtime.Profile.Id)) return;
        if (_nextPriceClaims.TryGetValue(runtime.Profile.Id, out var due) && due > DateTimeOffset.UtcNow) return;
        var brokerDue = !_nextBrokerRuns.TryGetValue(runtime.Profile.Id, out var brokerAt) || brokerAt <= DateTimeOffset.UtcNow;
        if (brokerDue && radar.IsInsideCenterZone) return;
        _priceWorkerProfiles.Add(runtime.Profile.Id);
        _ = brokerDue ? ReturnToCenterAsync(runtime) : RunLocalPriceWorkerAsync(runtime, radar);
    }

    private async Task RunLocalPriceWorkerAsync(ProfileRuntime runtime, RadarSnapshot radar)
    {
        IReadOnlyList<LocalPriceTarget> targets = [];
        try
        {
            if (runtime.ProcessId is not int pid || !runtime.IsCollectionEnabled) return;
            await EnsurePriceSessionAsync(runtime, pid);
            if (!runtime.IsCollectionEnabled || runtime.ProcessId != pid) return;
            targets = _localPriceQueue.Nearby(runtime.Profile.Id, radar, 145, 16);
            if (targets.Count > 0) await RunLocalPriceBatchAsync(runtime, pid, targets);
            else if (_localPriceQueue.Next(runtime.Profile.Id, radar) is { } next)
            {
                targets = [next];
                await RunTravelPriceJobAsync(runtime, pid, next);
            }
            else
            {
                runtime.Status = "Локальная очередь цен пуста";
                _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddSeconds(2);
                return;
            }
            _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow;
        }
        catch (Exception exception)
        {
            runtime.Status = "Ошибка чтения лавки · локальный повтор позже";
            Log($"{runtime.Profile.Name}: сбор цен — {exception.GetBaseException().Message.Trim()}");
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
        runtime.Status = $"Читаю рядом: {batch.Count} лавок";
        var folder = Path.Combine(AppContext.BaseDirectory, "price-snapshots"); Directory.CreateDirectory(folder);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var input = Path.Combine(folder, $"{runtime.Profile.Name}-local-{stamp}.input.json");
        var output = Path.Combine(folder, $"{runtime.Profile.Name}-local-{stamp}.json");
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(batch.Select(item => new
        {
            object_id = item.ObjectId, kiosk_type = item.KioskType, name = item.TraderName, x = item.X, y = item.Y
        })));
        await RunWorkerAsync(pid, "price-batch", output, start => { start.ArgumentList.Add("--input"); start.ArgumentList.Add(input); });
        var capture = JsonSerializer.Deserialize<PriceBatchCaptureFile>(await File.ReadAllTextAsync(output)) ?? throw new InvalidDataException("Batch Worker вернул пустой снимок");
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
        runtime.Status = $"Цены: {capture.Shops.Count}/{batch.Count} · {capture.ShopsPerSecond:F1} лавок/с";
        Log($"{runtime.Profile.Name}: локальная пачка {capture.Shops.Count}/{batch.Count} за {capture.ElapsedSeconds:F3} сек ({capture.ShopsPerSecond:F1}/с)");
    }

    private async Task RunTravelPriceJobAsync(ProfileRuntime runtime, int pid, LocalPriceTarget target)
    {
        runtime.Status = $"Иду к {target.TraderName} · попытка {target.AttemptCount + 1}";
        var folder = Path.Combine(AppContext.BaseDirectory, "price-snapshots"); Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, $"{runtime.Profile.Name}-{target.ObjectId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await RunWorkerAsync(pid, "price", output, start =>
        {
            start.ArgumentList.Add("--object-id"); start.ArgumentList.Add(target.ObjectId.ToString());
            start.ArgumentList.Add("--kiosk-type"); start.ArgumentList.Add(target.KioskType.ToString());
            start.ArgumentList.Add("--trader-name"); start.ArgumentList.Add(target.TraderName);
            start.ArgumentList.Add("--target-x"); start.ArgumentList.Add(target.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--target-y"); start.ArgumentList.Add(target.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
        var capture = JsonSerializer.Deserialize<ShopCaptureFile>(await File.ReadAllTextAsync(output)) ?? throw new InvalidDataException("Price Worker вернул пустой снимок");
        await _uploadOutbox.EnqueueLocalPriceResultAsync(runtime.Profile, target, capture);
        _localPriceQueue.Complete(runtime.Profile.Id, target.TraderKey);
        Log($"{runtime.Profile.Name}: {target.TraderName} проверен, {capture.Rows.Count} строк");
    }

    private async Task ReturnToCenterAsync(ProfileRuntime runtime)
    {
        try
        {
            if (runtime.ProcessId is not int pid || GetCenterZone(runtime.Profile) is not MarketZone center) return;
            await EnsurePriceSessionAsync(runtime, pid);
            runtime.Status = "Возвращаюсь в центр для обновления количеств…";
            var folder = Path.Combine(AppContext.BaseDirectory, "movement"); Directory.CreateDirectory(folder);
            var output = Path.Combine(folder, $"{runtime.Profile.Name}-center-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await RunWorkerAsync(pid, "move", output, start =>
            {
                start.ArgumentList.Add("--target-x"); start.ArgumentList.Add(center.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
                start.ArgumentList.Add("--target-y"); start.ArgumentList.Add(center.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
                start.ArgumentList.Add("--radius"); start.ArgumentList.Add("180");
            });
            runtime.Status = "В центре · обновляю количества брокером";
            _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow;
        }
        catch (Exception exception)
        {
            runtime.Status = "Не удалось вернуться в центр · повтор позже";
            Log($"{runtime.Profile.Name}: возврат в центр — {exception.GetBaseException().Message.Trim()}");
            _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddSeconds(5);
        }
        finally { _priceWorkerProfiles.Remove(runtime.Profile.Id); }
    }

    private static async Task RunWorkerAsync(int pid, string mode, string output, Action<ProcessStartInfo> configure)
    {
        var executable = BrokerRuntimeIsolation.WorkerFor(pid);
        var start = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--mode"); start.ArgumentList.Add(mode); start.ArgumentList.Add("--pid"); start.ArgumentList.Add(pid.ToString()); start.ArgumentList.Add("--output"); start.ArgumentList.Add(output); configure(start);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить встроенный Worker");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(); var stderrTask = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        var stdout = await stdoutTask; var stderr = await stderrTask;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    private async Task EnsurePriceSessionAsync(ProfileRuntime runtime, int pid)
    {
        if (_preparedPricePids.Contains(pid)) return;
        runtime.Status = "Подготовка единого коллектора…";
        await RunWorkerAsync(pid, "price-prepare", "NUL", _ => { });
        _preparedPricePids.Add(pid);
        Log($"{runtime.Profile.Name}: единая price-сессия PID {pid} готова");
    }

    private async Task CleanupPriceSessionAsync(ProfileRuntime runtime)
    {
        if (_priceWorkerProfiles.Contains(runtime.Profile.Id) || runtime.ProcessId is not int pid || !_preparedPricePids.Remove(pid)) return;
        var executable = BrokerRuntimeIsolation.WorkerFor(pid);
        var start = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--mode"); start.ArgumentList.Add("cleanup"); start.ArgumentList.Add("--pid"); start.ArgumentList.Add(pid.ToString()); start.ArgumentList.Add("--output"); start.ArgumentList.Add("NUL");
        using var process = Process.Start(start); if (process is not null) await process.WaitForExitAsync();
    }
}
