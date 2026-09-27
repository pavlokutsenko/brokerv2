using System.Diagnostics;
using PriceCheck.Contracts;
using PriceCheck.Collector.Runtime.Driver;

namespace PriceCheck.Windows;

public static class ClientProcessIdentity
{
    public static ClientSession? Read(int pid)
    {
        try
        {
            using var device = new Lu4Device();
            var status = device.QueryProcessStatus(pid);
            return status.Active && status.StartedAtUtc is { } created ? new(pid, created) : null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException) { }
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : new(pid, new DateTimeOffset(process.StartTime.ToUniversalTime()));
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    public static bool IsCurrent(ClientSession session) => Read(session.ProcessId) == session;

    public static IReadOnlyList<ClientSession> Discover() => Process.GetProcesses()
        .Select(process =>
        {
            using (process)
            {
                try { return process.ProcessName is "lu4" or "lu4.bin" ? Read(process.Id) : null; }
                catch (InvalidOperationException) { return null; }
            }
        }).OfType<ClientSession>().OrderBy(session => session.StartedAtUtc).ToArray();
}
