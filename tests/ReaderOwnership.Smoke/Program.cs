using System.Diagnostics;
using PriceCheck.Contracts;
using PriceCheck.Windows;

var identity = new ClientSession(123, DateTimeOffset.UnixEpoch);
if (args.Length == 2 && args[0] == "hold")
{
    using var held = ClientReaderLease.Acquire(identity, args[1]);
    Console.WriteLine("HELD");
    Console.In.ReadLine();
    return;
}
var directory = Path.Combine(Path.GetTempPath(), "PriceCheck-lease-tests", Guid.NewGuid().ToString("N"));
using var owner = Process.Start(new ProcessStartInfo { FileName = Environment.ProcessPath!,
    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
    ArgumentList = { "hold", directory } })!;
try
{
    if (await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) != "HELD") throw new Exception("Child did not acquire lease");
    try { using var duplicate = ClientReaderLease.Acquire(identity, directory); throw new Exception("Duplicate reader accepted"); }
    catch (InvalidOperationException) { }
    using var other = ClientReaderLease.Acquire(new(124, identity.StartedAtUtc), directory);
    using var generation = ClientReaderLease.Acquire(new(123, identity.StartedAtUtc.AddSeconds(1)), directory);
    owner.Kill(); // The synthetic child owns no game or native resources.
    await owner.WaitForExitAsync();
    using var recovered = ClientReaderLease.Acquire(identity, directory);
    Console.WriteLine("READER_OWNERSHIP_OK cross_process_exclusion independent_pids pid_generation crash_release");
}
finally { if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(); } }
