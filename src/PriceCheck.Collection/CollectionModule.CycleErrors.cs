using System.Net.Http;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private static bool IsUnsafeNativeFailure(Exception error)
    {
        var message=error.GetBaseException().Message;
        return message.Contains("ProcessEvent command bridge is busy:",StringComparison.Ordinal) ||
            message.Contains("game-thread ProcessEvent command was not consumed",StringComparison.Ordinal) ||
            message.Contains("Broker native cleanup failed; route must not start",StringComparison.Ordinal);
    }
    private void RecordCycleFailure(ProfileRuntime runtime, CycleRun cycle, Exception error)
    {
        if (!runtime.IsCollectionEnabled) return;
        if (File.Exists(cycle.StopFile))
        {
            runtime.IsCollectionEnabled = runtime.Profile.CollectionEnabled = false;
            cycle.Phase = "Stopped";
            runtime.Status = "Collection stopped";
            runtime.Cycle = runtime.Cycle with { Phase = "Stopped", Detail = runtime.Status };
            return;
        }
        var phase = cycle.Phase;
        var logPath = Path.Combine(cycle.Folder, $"error-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        try { File.WriteAllText(logPath, $"Phase: {phase}\nPID: {runtime.ProcessId}\n{error}"); }
        catch (Exception io) when (io is IOException or UnauthorizedAccessException)
        { Log($"Could not save cycle error: {io.Message}"); }
        var lines = error.GetBaseException().Message.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries);
        var summary = lines.FirstOrDefault(line => line.StartsWith("RuntimeError:", StringComparison.Ordinal))
            ?? lines.FirstOrDefault() ?? error.GetType().Name;
        if (summary.Length > 180) summary = summary[..180];
        Log($"{runtime.Profile.Name}: {phase} — {summary}. Details: {logPath}");
        if(IsUnsafeNativeFailure(error))
        {
            // Do not reuse or reset an armed/running native command. Owned-client
            // recovery drains the reader and resumes this same cycle on a new PID.
            RequestCycleRecovery(runtime,cycle,summary,unsafeCommand:true);
            summary=runtime.Status;
        }
        else if (error is HttpRequestException or TaskCanceledException)
        {
            if (phase != "Waiting for server") cycle.ResumePhase = phase;
            cycle.Phase = "Waiting for server";
            cycle.Next = DateTimeOffset.UtcNow.AddSeconds(15);
        }
        else if (phase == "Broker inventory")
        {
            cycle.Next=DateTimeOffset.UtcNow.AddSeconds(15);
            summary=$"WARNING Center broker/radar failed; retaining history and retrying before price movement. {summary}";
            if(++cycle.BrokerFailures>=3)
            {
                RequestCycleRecovery(runtime,cycle,summary);
                summary=runtime.Status;
            }
        }
        else if (phase == "Return to center" && ++cycle.ReturnFailures >= 3)
        {
            RequestCycleRecovery(runtime,cycle,summary);
            summary=runtime.Status;
        }
        else if (phase == "Reading prices")
        {
            cycle.Next=DateTimeOffset.UtcNow.AddSeconds(1);
            summary=$"Price operation failed; replanning remaining targets. {summary}";
        }
        else { cycle.Phase = "Return to center"; cycle.Next = DateTimeOffset.UtcNow.AddSeconds(30); }
        runtime.Status = summary;
        runtime.Cycle = runtime.Cycle with { Phase = cycle.Phase, Detail = summary };
    }

    private void RequestCycleRecovery(ProfileRuntime runtime,CycleRun cycle,string reason,bool unsafeCommand=false)
    {
        if(runtime.Profile.AutoRestartEnabled || unsafeCommand)
        {
            runtime.ClientFault=reason;
            File.WriteAllText(cycle.StopFile,"owned client recovery required");
            cycle.Phase="Client recovery";
            runtime.Status=runtime.Profile.AutoRestartEnabled
                ? $"Recovering client; collection will resume. {reason}"
                : $"Client recovery required; enable automatic restart. {reason}";
        }
        else
        {
            runtime.IsCollectionEnabled=runtime.Profile.CollectionEnabled=false;
            cycle.Phase="Stopped";
            runtime.Status=$"Collection stopped; automatic restart is disabled. {reason}";
        }
    }
}
