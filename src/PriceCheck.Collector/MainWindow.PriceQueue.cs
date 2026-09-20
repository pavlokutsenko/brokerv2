using System.Diagnostics;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collector;

public partial class MainWindow
{
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
        PriceQueueJob? job = null;
        var workerId = $"{Environment.MachineName}:{runtime.Profile.Id:N}";
        try
        {
            if (runtime.ProcessId is not int pid || !runtime.IsCollectionEnabled) return;
            await EnsurePriceSessionAsync(runtime, pid);
            if (!runtime.IsCollectionEnabled || runtime.ProcessId != pid) return;
            runtime.Status = "Очередь цен: получаю задачу…";
            job = await _marketApi.ClaimPriceJobAsync(runtime.Profile, radar, workerId);
            if (job is null)
            {
                runtime.Status = "Очередь цен пуста";
                _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow.AddSeconds(3);
                return;
            }
            if (!long.TryParse(job.ObjectId, out var objectId) || objectId <= 0)
                throw new InvalidDataException("У задачи нет актуального ObjectID");

            runtime.Status = $"Иду к {job.TraderName} · попытка {job.AttemptCount}";
            Log($"{runtime.Profile.Name}: взят {job.TraderName} ({job.TraderId})");
            var executable = BrokerRuntimeIsolation.WorkerFor(pid);
            var outputFolder = Path.Combine(AppContext.BaseDirectory, "price-snapshots");
            Directory.CreateDirectory(outputFolder);
            var output = Path.Combine(outputFolder, $"{runtime.Profile.Name}-{job.TraderId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            var start = new ProcessStartInfo
            {
                FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add("--mode"); start.ArgumentList.Add("price");
            start.ArgumentList.Add("--pid"); start.ArgumentList.Add(pid.ToString());
            start.ArgumentList.Add("--object-id"); start.ArgumentList.Add(objectId.ToString());
            start.ArgumentList.Add("--output"); start.ArgumentList.Add(output);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить встроенный Price Worker");
            using var heartbeatCancellation = new CancellationTokenSource();
            var heartbeat = KeepLeaseAliveAsync(runtime.Profile, job, workerId, heartbeatCancellation.Token);
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            heartbeatCancellation.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
            var stdout = await stdoutTask; var stderr = await stderrTask;
            if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            var capture = JsonSerializer.Deserialize<ShopCaptureFile>(await File.ReadAllTextAsync(output)) ?? throw new InvalidDataException("Price Worker вернул пустой снимок");
            if (capture.Rows.Count == 0) throw new InvalidDataException("Лавка прочитана без строк");
            await _marketApi.SubmitPriceJobAsync(runtime.Profile, job, capture, workerId);
            runtime.Status = $"Цена сохранена: {job.TraderName} · {capture.Rows.Count} строк";
            Log($"{runtime.Profile.Name}: {job.TraderName} проверен, {capture.Rows.Count} строк");
            _nextPriceClaims[runtime.Profile.Id] = DateTimeOffset.UtcNow;
        }
        catch (Exception exception)
        {
            var message = exception.GetBaseException().Message.Trim();
            runtime.Status = "Ошибка чтения лавки · повтор будет позже";
            Log($"{runtime.Profile.Name}: сборщик цен — {message}");
            if (job is not null)
            {
                try { await _marketApi.FailPriceJobAsync(runtime.Profile, job, workerId, message); }
                catch (Exception reportError) { Log($"{runtime.Profile.Name}: не удалось вернуть задачу — {reportError.GetBaseException().Message}"); }
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
