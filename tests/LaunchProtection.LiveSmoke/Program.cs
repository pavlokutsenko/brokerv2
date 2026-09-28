using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PriceCheck.Collector.Services;
using PriceCheck.Collector.Runtime.Driver;
using PriceCheck.Collector.Runtime.Radar;
using PriceCheck.Contracts;
using PriceCheck.Launching;
using PriceCheck.Windows;

// Proxy credentials arrive through redirected stdin; never persist or print them.
if (args.Contains("--banner-child")) { await ProxyBannerProbe.ChildAsync(); return; }
var profileId = args.Length == 1 && !args[0].StartsWith("--") ? Guid.Parse(args[0]) :
    Guid.Parse("6a0358a7-984d-4ef1-8570-283b79c3cf88");
var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceCheckCollector");
var settings = new[] { "profiles.json", "launch-templates.json" }.Select(name => Path.Combine(directory, name)).ToArray();
var hashes = settings.Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
var profile = (await new ProfileStore().LoadAsync()).Single(p => p.Id == profileId);
var template = (await new LaunchTemplateStore().LoadAsync()).Single(t => t.Id == profile.LaunchTemplateId);
if (!args.Contains("--direct") && !args.Contains("--saved-proxy")) {
template.ProxyHost = Console.ReadLine() ?? throw new ArgumentException("Proxy host is required.");
template.ProxyPort = int.Parse(Console.ReadLine()!, CultureInfo.InvariantCulture);
template.ProxyUser = Console.ReadLine() ?? "";
template.ProxyPassword = Console.ReadLine() ?? "";
template.ProxyEnabled = true;
} else if (args.Contains("--direct")) {
    template.ProxyEnabled = false;
    // Invalid stale values prove that disabled fields are never validated or contacted.
    template.ProxyHost = "invalid host:unused"; template.ProxyPort = -1;
    template.ProxyUser = "unused\r\n"; template.ProxyPassword = "unused\r\n";
}
template.HardwareEnabled = true;
template.RotateEachLaunch = false;
if (args.Contains("--banner"))
{
    await ProxyBannerProbe.RunAsync(template);
    return;
}
if (args.Contains("--wfp-banner"))
{
    await ProxyBannerProbe.WfpAsync(template);
    return;
}
profile.CollectionEnabled = false;
profile.LastProcessId = null; profile.LastProcessStartUtc = null;
profile.CharacterRotationEnabled = !args.Contains("--no-rotation");
profile.CharacterSlot = 0;
Environment.SetEnvironmentVariable("PRICECHECK_TRACE_HARDWARE", "1");
Environment.SetEnvironmentVariable("PRICECHECK_NETWORK_OBSERVE", "1");
new DriverBootstrapper().EnsureReady();
if (args.Contains("--baseline") || args.Contains("--early-route") || args.Contains("--native-gates") || args.Contains("--proxy-probe") || args.Contains("--managed-guard"))
{
    try { await IncrementalLaunchProbe.RunAsync(profile, template, !args.Contains("--baseline"),
        args.Contains("--native-gates") || args.Contains("--proxy-probe") || args.Contains("--managed-guard"),
        args.Contains("--proxy-probe"), args.Contains("--managed-guard")); }
    finally
    {
        if (!settings.Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).SequenceEqual(hashes))
            throw new InvalidOperationException("Production settings changed during baseline.");
        Console.WriteLine("PRODUCTION_SETTINGS_UNCHANGED");
    }
    return;
}
var launcher = new LaunchModule();
var rotation = new CharacterRotationService(launcher);
await using var radar = new RadarSessionManager();
using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(7));
using var pulseStop = new CancellationTokenSource();
void Log(string text) => Console.WriteLine($"{DateTimeOffset.UtcNow:O} {text}");
void Status(string phase) => Log(phase + " " + JsonSerializer.Serialize(launcher.Protection(profile.Id)));
var pulse = Task.Run(async () =>
{
    ulong seen = ulong.MaxValue;
    bool lifetimeReported = false;
    try
    {
        while (!pulseStop.IsCancellationRequested)
        {
            if (profile.LastProcessId is int pid)
            {
                using var device = new Lu4Device();
                var route = device.QueryProxyGuard(pid);
                var life = device.QueryProcessStatus(pid);
                bool ordinaryAlive;
                try { using var process = Process.GetProcessById(pid); ordinaryAlive = !process.HasExited; }
                catch { ordinaryAlive = false; }
                if (life.Active && !ordinaryAlive && !lifetimeReported)
                {
                    lifetimeReported = true;
                    Log("KERNEL_LIFETIME_VERIFIED process is active despite unavailable external process handle");
                }
                if (seen != route.BlockedConnections)
                {
                    seen = route.BlockedConnections;
                    Log("NETWORK_POLICY " + JsonSerializer.Serialize(route));
                }
            }
            await Task.Delay(500, pulseStop.Token);
        }
    }
    catch (OperationCanceledException) { }
});
async Task World(ClientSession session)
{
    await launcher.ValidateProtectionAsync(profile.Id, true, stop.Token);
    await radar.StartAsync(session.ProcessId, null, false, stop.Token);
    var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
    while (DateTimeOffset.UtcNow < deadline)
    {
        var snapshot = radar.Snapshot(session.ProcessId, null, false);
        if (snapshot is not null && Math.Abs(snapshot.PlayerX) > 100 && Math.Abs(snapshot.PlayerY) > 100)
        {
            Log($"WORLD slot={profile.CharacterSlot} roster={profile.RotationCharacterCount} pid={session.ProcessId} x={snapshot.PlayerX:F0} y={snapshot.PlayerY:F0}");
            Status("PROTECTED_WORLD");
            return;
        }
        await Task.Delay(300, stop.Token);
    }
    throw new TimeoutException("Protected client did not report a player position.");
}
try
{
    Log("COLLECTOR_FLOW protected reader before login");
    var session = await launcher.LaunchAsync(profile, template, Log, stop.Token,
        async (client, token) => { await radar.StartAsync(client.ProcessId, null, false, token); Log("READER_ATTACHED_AFTER_PROTECTION"); });
    rotation.Started(profile, session, DateTimeOffset.UtcNow);
    await World(session);
    for (var i = 0; i < 6; i++)
    {
        await Task.Delay(2000, stop.Token);
        await launcher.ValidateProtectionAsync(profile.Id, true, stop.Token);
        Status("CONTINUOUS_CHECK");
    }
    if (profile.RotationCharacterCount < 2) throw new InvalidOperationException("A second character is required for rotation acceptance.");
    var old = session;
    Log("LAUNCHER_FLOW rotation without reader before login");
    session = await rotation.RotateAsync(profile, old, template,
        () => radar.StopAsync(old.ProcessId), World, Log, stop.Token);
    if (ClientProcessIdentity.IsCurrent(old) || profile.CharacterSlot != 1)
        throw new InvalidOperationException("Character rotation retained the old process or selected the wrong slot.");
    await Task.Delay(12000, stop.Token);
    await launcher.ValidateProtectionAsync(profile.Id, true, stop.Token);
    Status("ROTATED_WORLD");
    Log("FAULT revoke only the route owned by this test");
    using (var device = new Lu4Device())
    {
        var route = device.QueryProxyGuard(session.ProcessId);
        if (!route.Active || route.HostProcessId != Environment.ProcessId) throw new InvalidOperationException("Test does not own the route.");
        device.SetProxyRedirect(session.ProcessId, route.ListenerPort, false);
    }
    var failedBy = DateTimeOffset.UtcNow.AddSeconds(6);
    while (DateTimeOffset.UtcNow < failedBy &&
        (!launcher.Protection(profile.Id).Failed || ClientProcessIdentity.IsCurrent(session)))
        await Task.Delay(100, stop.Token);
    Status("REVOKED_ROUTE");
    if (!launcher.Protection(profile.Id).Failed || ClientProcessIdentity.IsCurrent(session))
        throw new InvalidOperationException("Route revocation did not stop the live client.");
    try
    {
        await launcher.ValidateProtectionAsync(profile.Id, true, stop.Token);
        throw new InvalidOperationException("Character gate allowed a stopped client.");
    }
    catch (LaunchProtectionException) { Log("CHARACTER_GATE_REJECTED"); }
    await radar.StopAsync(session.ProcessId);
    Log("LAUNCH_PROTECTION_LIVE_OK");
}
catch (Exception error)
{
    Status("LIVE_FAILURE");
    Log(error.ToString());
    Environment.ExitCode = 1;
}
finally
{
    pulseStop.Cancel(); await pulse;
    launcher.StopOwnedClients();
    if (!settings.Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).SequenceEqual(hashes))
        throw new InvalidOperationException("Production settings changed during the probe.");
    Log("PRODUCTION_SETTINGS_UNCHANGED");
}
