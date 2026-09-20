using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private sealed record LeasedPriceJob(
        PriceQueueJob Job, string WorkerId, long ObjectId, int KioskType,
        string TraderName, double? X, double? Y);

    private void TryStartPriceWorker(ProfileRuntime runtime, RadarSnapshot radar)
    {
        if (runtime.Profile.Role != CollectorRole.PriceVerifier || !runtime.IsCollectionEnabled ||
            runtime.ProcessId is not int || _priceWorkerProfiles.Contains(runtime.Profile.Id)) return;
        if (_nextPriceClaims.TryGetValue(runtime.Profile.Id, out var due) && due > DateTimeOffset.UtcNow) return;
        _priceWorkerProfiles.Add(runtime.Profile.Id);
        _ = RunPriceWorkerAsync(runtime, radar);
    }

    private async Task RunPriceWorkerAsync(ProfileRuntime runtime, RadarSnapshot radar)
    {
        var leases = new List<LeasedPriceJob>();
        var settled = new HashSet<string>(StringComparer.Ordinal);
        var workerBase = $"{Environment.MachineName}:{runtime.Profile.Id:N}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        try
        {
            if (runtime.ProcessId is not int pid || !runtime.IsCollectionEnabled) return;
            await EnsurePriceSessionAsync(runtime, pid);
            if (!runtime.IsCollectionEnabled || runtime.ProcessId != pid) return;
            runtime.Status = "Очередь цен: получаю задачу…";
            var first = await ClaimLeaseAsync(runtime, radar, workerBase, 0);
            if (first is null)
            {
                runtime.Status = "Очередь цен пуста";
                _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddSeconds(3);
                return;
            }
            leases.Add(first);
            var local = IsLocal(first, radar, 95);
            LeasedPriceJob? travel = local ? null : first;
            if (local)
            {
                for (var index = 1; index < 8; index++)
                {
                    var next = await ClaimLeaseAsync(runtime, radar, workerBase, index);
                    if (next is null) break;
                    leases.Add(next);
                    if (!IsLocal(next, radar, 95)) { travel = next; break; }
                }
                var batch = travel is null ? leases : leases.Where(item => item != travel).ToList();
                if (batch.Count > 0) await RunLocalPriceBatchAsync(runtime, pid, batch, settled);
            }
            if (travel is not null) await RunTravelPriceJobAsync(runtime, pid, travel, settled);
            _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow;
        }
        catch (Exception exception)
        {
            var message = exception.GetBaseException().Message.Trim();
            runtime.Status = "Ошибка чтения лавки · повтор будет позже";
            Log($"{runtime.Profile.Name}: сборщик цен — {message}");
            foreach (var lease in leases.Where(item => !settled.Contains(item.Job.TraderId)))
            {
                try { await _uploadOutbox.EnqueuePriceFailureAsync(runtime.Profile, lease.Job, lease.WorkerId, message); settled.Add(lease.Job.TraderId); }
                catch (Exception reportError) { Log($"{runtime.Profile.Name}: не удалось записать возврат задачи — {reportError.GetBaseException().Message}"); }
            }
            _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddSeconds(3);
        }
        finally
        {
            _priceWorkerProfiles.Remove(runtime.Profile.Id);
            if (!runtime.IsCollectionEnabled || runtime.Profile.Role != CollectorRole.PriceVerifier)
                await CleanupPriceSessionAsync(runtime);
        }
    }

    private async Task<LeasedPriceJob?> ClaimLeaseAsync(ProfileRuntime runtime, RadarSnapshot radar, string workerBase, int index)
    {
        var workerId = $"{workerBase}:{index}";
        var job = await _marketApi.ClaimPriceJobAsync(runtime.Profile, radar, workerId);
        if (job is null) return null;
        var radarTrader = radar.Traders
            .Where(item => item.IsVisible)
            .FirstOrDefault(item =>
                item.Name.Equals(job.TraderKey, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Equals(job.TraderName, StringComparison.OrdinalIgnoreCase));
        var objectId = radarTrader is not null
            ? radarTrader.ObjectId
            : long.TryParse(job.ObjectId, out var serverObjectId) ? serverObjectId : 0;
        if (objectId <= 0)
        {
            await _uploadOutbox.EnqueuePriceFailureAsync(runtime.Profile, job, workerId, "У задачи нет актуального ObjectID");
            return null;
        }
        var name = radarTrader?.Name ?? job.TraderName;
        var kioskType = radarTrader?.KioskType ?? job.KioskType;
        var x = radarTrader?.X ?? job.X;
        var y = radarTrader?.Y ?? job.Y;
        Log($"{runtime.Profile.Name}: взят {name} ({job.TraderId})" +
            (radarTrader is null ? " · координаты сервера" : " · найден локальным радаром"));
        return new LeasedPriceJob(job, workerId, objectId, kioskType, name, x, y);
    }

    private static bool IsLocal(LeasedPriceJob lease, RadarSnapshot radar, double radius) =>
        lease.X is double x && lease.Y is double y && Math.Sqrt(Math.Pow(x - radar.PlayerX, 2) + Math.Pow(y - radar.PlayerY, 2)) <= radius;

    private async Task RunLocalPriceBatchAsync(ProfileRuntime runtime, int pid, IReadOnlyList<LeasedPriceJob> batch, HashSet<string> settled)
    {
        runtime.Status = $"Читаю пачку: {batch.Count} лавок";
        var folder = Path.Combine(AppContext.BaseDirectory, "price-snapshots"); Directory.CreateDirectory(folder);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var input = Path.Combine(folder, $"{runtime.Profile.Name}-batch-{stamp}.input.json");
        var output = Path.Combine(folder, $"{runtime.Profile.Name}-batch-{stamp}.json");
        var targets = batch.Select(item => new { object_id = item.ObjectId, kiosk_type = item.KioskType, name = item.TraderName, x = item.X, y = item.Y }).ToArray();
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(targets));
        await RunWorkerAsync(pid, "price-batch", output, start => { start.ArgumentList.Add("--input"); start.ArgumentList.Add(input); });
        var capture = JsonSerializer.Deserialize<PriceBatchCaptureFile>(await File.ReadAllTextAsync(output)) ?? throw new InvalidDataException("Batch Worker вернул пустой снимок");
        var shops = capture.Shops.ToDictionary(item => item.Trader.ObjectId);
        var failures = capture.Failures.ToDictionary(item => item.ObjectId, item => item.Error);
        foreach (var lease in batch)
        {
            if (shops.TryGetValue(lease.ObjectId, out var shop) && shop.Rows.Count > 0)
                await _uploadOutbox.EnqueuePriceResultAsync(runtime.Profile, lease.Job, new ShopCaptureFile { Side = shop.Side, Rows = shop.Rows }, lease.WorkerId);
            else
                await _uploadOutbox.EnqueuePriceFailureAsync(runtime.Profile, lease.Job, lease.WorkerId, failures.GetValueOrDefault(lease.ObjectId, "Лавка не ответила в пакетном цикле"));
            settled.Add(lease.Job.TraderId);
        }
        runtime.Status = $"Пачка: {capture.Shops.Count}/{batch.Count} · {capture.ShopsPerSecond:F1} лавок/с";
        Log($"{runtime.Profile.Name}: пачка {capture.Shops.Count}/{batch.Count} за {capture.ElapsedSeconds:F3} сек ({capture.ShopsPerSecond:F1}/с)");
    }

    private async Task RunTravelPriceJobAsync(ProfileRuntime runtime, int pid, LeasedPriceJob lease, HashSet<string> settled)
    {
        runtime.Status = $"Иду к {lease.TraderName} · попытка {lease.Job.AttemptCount}";
        var folder = Path.Combine(AppContext.BaseDirectory, "price-snapshots"); Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, $"{runtime.Profile.Name}-{lease.Job.TraderId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        using var heartbeatCancellation = new CancellationTokenSource();
        var heartbeat = KeepLeaseAliveAsync(runtime.Profile, lease.Job, lease.WorkerId, heartbeatCancellation.Token);
        try
        {
            await RunWorkerAsync(pid, "price", output, start =>
            {
                start.ArgumentList.Add("--object-id"); start.ArgumentList.Add(lease.ObjectId.ToString());
                start.ArgumentList.Add("--kiosk-type"); start.ArgumentList.Add(lease.KioskType.ToString());
                start.ArgumentList.Add("--trader-name"); start.ArgumentList.Add(lease.TraderName);
                if (lease.X is double x) { start.ArgumentList.Add("--target-x"); start.ArgumentList.Add(x.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                if (lease.Y is double y) { start.ArgumentList.Add("--target-y"); start.ArgumentList.Add(y.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            });
        }
        finally { heartbeatCancellation.Cancel(); try { await heartbeat; } catch (OperationCanceledException) { } }
        var capture = JsonSerializer.Deserialize<ShopCaptureFile>(await File.ReadAllTextAsync(output)) ?? throw new InvalidDataException("Price Worker вернул пустой снимок");
        await _uploadOutbox.EnqueuePriceResultAsync(runtime.Profile, lease.Job, capture, lease.WorkerId); settled.Add(lease.Job.TraderId);
        Log($"{runtime.Profile.Name}: {lease.TraderName} проверен, {capture.Rows.Count} строк");
    }

    private static async Task RunWorkerAsync(int pid, string mode, string output, Action<ProcessStartInfo> configure)
    {
        var executable = BrokerRuntimeIsolation.WorkerFor(pid);
        var start = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--mode"); start.ArgumentList.Add(mode); start.ArgumentList.Add("--pid"); start.ArgumentList.Add(pid.ToString()); start.ArgumentList.Add("--output"); start.ArgumentList.Add(output); configure(start);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить встроенный Price Worker");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(); var stderrTask = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        var stdout = await stdoutTask; var stderr = await stderrTask; if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    private async Task EnsurePriceSessionAsync(ProfileRuntime runtime, int pid)
    {
        if (_preparedPricePids.Contains(pid)) return;
        runtime.Status = "Подготовка сборщика цен…";
        Log($"{runtime.Profile.Name}: подготавливаю price-сессию PID {pid}");
        var executable = BrokerRuntimeIsolation.WorkerFor(pid);
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--mode"); start.ArgumentList.Add("price-prepare");
        start.ArgumentList.Add("--pid"); start.ArgumentList.Add(pid.ToString());
        start.ArgumentList.Add("--output"); start.ArgumentList.Add("NUL");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось подготовить Price Worker");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        _preparedPricePids.Add(pid);
        runtime.Status = "Сборщик цен готов";
        Log($"{runtime.Profile.Name}: price-сессия PID {pid} готова");
    }

    private async Task KeepLeaseAliveAsync(CollectorProfile profile, PriceQueueJob job, string workerId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await _marketApi.HeartbeatPriceJobAsync(profile, job, workerId, cancellationToken);
    }

    private async Task CleanupPriceSessionAsync(ProfileRuntime runtime)
    {
        if (_priceWorkerProfiles.Contains(runtime.Profile.Id) || runtime.ProcessId is not int pid) return;
        _preparedPricePids.Remove(pid);
        var executable = BrokerRuntimeIsolation.WorkerFor(pid);
        var start = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--mode"); start.ArgumentList.Add("cleanup");
        start.ArgumentList.Add("--pid"); start.ArgumentList.Add(pid.ToString());
        start.ArgumentList.Add("--output"); start.ArgumentList.Add("NUL");
        using var process = Process.Start(start);
        if (process is not null) await process.WaitForExitAsync();
    }
}
