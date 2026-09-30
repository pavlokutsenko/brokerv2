using System.Diagnostics;
using PriceCheck.Contracts;
using PriceCheck.Collector.Runtime.Driver;
using PriceCheck.Collector.Services;

if (args.Contains("--job-child"))
{
    await Task.Delay(TimeSpan.FromSeconds(30));
    return;
}
if (args.Contains("--job"))
{
    var executable = Environment.ProcessPath ?? throw new Exception("No apphost for job smoke.");
    var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)! };
    start.ArgumentList.Add("--job-child");
    using var child = new SuspendedClientProcess(start);
    using var job = ClientResourceJob.Assign(child, 3072, 20);
    try
    {
        child.Resume();
        if (!job.Contains(child.Process.Id)) throw new Exception("Child did not inherit the job.");
        if (job.ReadCpuPercent() is not null) throw new Exception("CPU must stay unrestricted during process startup.");
        if (job.ApplyCpuBudget() != 20 || job.ReadCpuPercent() != 20)
            throw new Exception("Job CPU rate was not confirmed after startup.");
        var childSession = new ClientSession(child.Process.Id,
            new DateTimeOffset(child.Process.StartTime.ToUniversalTime()));
        var bounds = Lu4Device.ReadWorkingSetBounds(childSession);
        if (bounds.MaximumBytes != 3072UL * 1024 * 1024)
            throw new Exception($"Job maximum not visible: {bounds.MaximumBytes}.");
        Console.WriteLine($"RESOURCE_JOB_OK pid={child.Process.Id} maximum={bounds.MaximumBytes} flags={bounds.Flags} cpu=20% after startup");
    }
    finally { if (!child.Process.HasExited) child.Process.Kill(); }
    return;
}

using var process = Process.GetCurrentProcess();
var session = new ClientSession(process.Id, new DateTimeOffset(process.StartTime.ToUniversalTime()));
var original = Lu4Device.ReadWorkingSetBounds(session);
if (original.MinimumBytes == 0 || original.MaximumBytes < original.MinimumBytes) throw new Exception("Invalid readback.");
try
{
    Lu4Device.ReadWorkingSetBounds(session with { StartedAtUtc = session.StartedAtUtc.AddSeconds(-1) });
    throw new Exception("PID reuse guard accepted an incorrect birth time.");
}
catch (IOException) { }
if (!args.Contains("--driver"))
{
    Console.WriteLine($"WORKING_SET_QUERY_OK minimum={original.MinimumBytes} maximum={original.MaximumBytes} flags={original.Flags}; birth guard passed; no policy changed");
    return;
}
// Explicit synthetic-process test, for a legitimately installed candidate driver.
// This process owns itself; no game process, proxy route or collector attachment is involved.
using var device = new Lu4Device();
var saved = device.ApplyWorkingSetBudget(session, 256);
try
{
    var bytes = new byte[64 * 1024 * 1024];
    for (var i = 0; i < bytes.Length; i += 4096) bytes[i] = 37;
    var actual = Lu4Device.ReadWorkingSetBounds(session);
    if (actual.MaximumBytes != 256ul * 1024 * 1024 || (actual.Flags & 4) == 0) throw new Exception("Driver budget was not confirmed.");
    for (var i = 0; i < bytes.Length; i += 4096) if (bytes[i] != 37) throw new Exception("Allocation contents changed.");
    GC.KeepAlive(bytes);
}
finally { device.RestoreWorkingSetBudget(session, saved); }
if (Lu4Device.ReadWorkingSetBounds(session) != original) throw new Exception("Original bounds not restored.");
Console.WriteLine("WORKING_SET_DRIVER_OK budget confirmed, allocation contents intact, original bounds restored");
