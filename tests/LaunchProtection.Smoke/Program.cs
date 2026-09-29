if (args.Length != 0 && args[0].StartsWith("--child", StringComparison.Ordinal))
{
    if (args[0] is "--child-suspended" or "--child-wait" or "--child-world" or "--child-direct")
    { await GuardMonitorChecks.ChildAsync(args); return; }
    await GuardNetworkChecks.ChildAsync(args); return;
}
if (args.Contains("--root-reuse")) { await ReusedRootChecks.RunAsync(); return; }
await GuardPhaseChecks.RunAsync();
await DnsRelayChecks.RunAsync();
await OptionalProxyChecks.ConfigurationAsync();
if (args.Contains("--wfp")) { await ProcessLifetimeChecks.RunAsync(); await GuardNetworkChecks.RunAsync(); await GuardMonitorChecks.RunAsync(); await OptionalProxyChecks.TransportAsync(); await AgentReadinessChecks.RunAsync(); await ReusedRootChecks.RunAsync(); }
Console.WriteLine("LaunchProtection.Smoke: PASS");
