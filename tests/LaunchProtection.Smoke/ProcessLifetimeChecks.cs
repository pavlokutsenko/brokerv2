using System.Diagnostics;
using PriceCheck.Collector.Runtime.Driver;
using PriceCheck.Windows;

internal static class ProcessLifetimeChecks
{
    internal static async Task RunAsync()
    {
        using var device = new Lu4Device();
        using var current = Process.GetCurrentProcess();
        var own = device.QueryProcessStatus(current.Id);
        if (!own.Active || own.StartedAtUtc?.UtcDateTime != current.StartTime.ToUniversalTime())
            throw new Exception("Kernel creation time differs from Windows process identity.");
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, RedirectStandardInput = true, ArgumentList = { "--child-wait" }
        }) ?? throw new Exception("Lifetime child did not start.");
        try
        {
            var session = ClientProcessIdentity.Read(child.Id) ?? throw new Exception("Live process was not identified.");
            child.Kill(); await child.WaitForExitAsync();
            if (device.QueryProcessStatus(child.Id).Active || ClientProcessIdentity.IsCurrent(session))
                throw new Exception("Exited process retained a valid protection identity.");
            if (device.QueryProcessStatus(int.MaxValue).Active)
                throw new Exception("Absent process was reported active.");
        }
        finally { if (!child.HasExited) child.Kill(); }
        Console.WriteLine("KERNEL_LIFETIME_OK exact birth, live PID, exited PID and absent PID");
    }
}
