if (args.Length != 0 && args[0].StartsWith("--child", StringComparison.Ordinal))
{
    if (args[0] is "--child-suspended" or "--child-wait" or "--child-world")
    { await GuardMonitorChecks.ChildAsync(args); return; }
    await GuardNetworkChecks.ChildAsync(args); return;
}
await GuardPhaseChecks.RunAsync();
await DnsRelayChecks.RunAsync();
if (args.Contains("--wfp")) { await ProcessLifetimeChecks.RunAsync(); await GuardNetworkChecks.RunAsync(); await GuardMonitorChecks.RunAsync(); }
Console.WriteLine("LaunchProtection.Smoke: PASS");
