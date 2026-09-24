using System.Diagnostics;
using PriceCheck.Contracts;

namespace PriceCheck.Windows;

public static class ClientProcessIdentity
{
    public static ClientSession? Read(int pid)
    {
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
