using System.Diagnostics;
using PriceCheck.Contracts;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

internal sealed class CollectionWorker(Func<ClientSession, bool> isCurrent) : ICollectionWorker
{
    public async Task RunAsync(ClientSession session, string mode, string output, Action<ProcessStartInfo> configure)
    {
        if (!isCurrent(session)) throw new OperationCanceledException("Client exited or changed.");
        var executable = BrokerRuntimeIsolation.WorkerFor(session);
        var start = new ProcessStartInfo
        {
            FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--mode"); start.ArgumentList.Add(mode);
        start.ArgumentList.Add("--pid"); start.ArgumentList.Add(session.ProcessId.ToString());
        start.ArgumentList.Add("--output"); start.ArgumentList.Add(output);
        configure(start);
        if (!isCurrent(session)) throw new OperationCanceledException("Client changed during worker setup.");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start collector worker.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExitAsync();
        var plannerDeadline=DateTimeOffset.UtcNow.AddSeconds(60);
        while (!exited.IsCompleted)
        {
            await Task.WhenAny(exited, Task.Delay(250));
            if(mode=="market-plan" && (DateTimeOffset.UtcNow>plannerDeadline ||
                start.Environment.TryGetValue("PRICECHECK_STOP_FILE",out var stopFile) && File.Exists(stopFile)))
            {
                if(!process.HasExited)process.Kill(entireProcessTree:true);
                await exited;
                throw new OperationCanceledException("Offline route preparation stopped or exceeded60 seconds.");
            }
            if (isCurrent(session)) continue;
            // Only the worker is stopped. The target game has already exited or changed.
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await exited;
            throw new OperationCanceledException("Client exited or changed while collecting.");
        }
        await exited;
        var error = await stderr;
        var standardOutput = await stdout;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? standardOutput : error);
    }
}
