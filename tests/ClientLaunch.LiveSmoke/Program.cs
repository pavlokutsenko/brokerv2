using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Collector.Runtime.Radar;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Runtime.InteropServices;

if (args.Length is < 1 or > 2 || !File.Exists(args[0]) ||
    (args.Length == 2 && args[1] is not ("--observe" or "--observe-direct" or "--observe-real-proxy" or "--observe-early-proxy-only" or "--auto-login" or "--auto-login-saved" or "--auto-login-real-proxy" or "--auto-login-real-proxy-saved" or "--auto-login-local-proxy" or "--auto-login-local-proxy-saved" or "--auto-login-early-proxy-only" or "--auto-login-black-template" or "--auto-login-black-direct" or "--auto-login-black-isolated-data" or "--two-clients")))
    throw new ArgumentException("Pass lu4-win64-shipping.exe and optional observation or --auto-login mode");

if (args.Length == 2 && args[1] == "--two-clients")
{
    Environment.SetEnvironmentVariable("PRICECHECK_TRACE_HARDWARE", "1");
    using var reservation = new TcpListener(IPAddress.Loopback, 0);
    reservation.Start();
    var proxyPort = ((IPEndPoint)reservation.LocalEndpoint).Port;
    reservation.Stop();
    var twoClientProxyLog = Path.Combine(Path.GetTempPath(), $"pricecheck-two-clients-{Environment.ProcessId}.txt");
    _ = Task.Run(() => ProxyServer.RunAsync(proxyPort, twoClientProxyLog));
    await Task.Delay(300);

    var twoClientService = new ClientProcessService();
    await using var twoClientRadar = new RadarSessionManager();
    var launched = new List<int>();
    using var twoClientTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
    try
    {
        for (var index = 0; index < 2; index++)
        {
            var launchProfile = new CollectorProfile { LaunchFile = Path.GetFullPath(args[0]) };
            var launchTemplate = new LaunchTemplate
            {
                Name = $"Two-client test {index + 1}",
                HardwareEnabled = true,
                RotateEachLaunch = true,
                ProxyEnabled = true,
                ProxyHost = "127.0.0.1",
                ProxyPort = proxyPort,
                ProxyUser = "user",
                ProxyPassword = "pass"
            };
            var launchedPid = await twoClientService.LaunchAndBindAsync(
                launchProfile, launchTemplate, twoClientTimeout.Token);
            launched.Add(launchedPid);
            await twoClientService.WaitForGameWindowAsync(launchedPid, twoClientTimeout.Token);
            await twoClientRadar.StartAsync(launchedPid, null, false, twoClientTimeout.Token);
            using var ready = EventWaitHandle.OpenExisting($@"Local\PriceCheckAgentReady_{launchedPid}");
            if (!ready.WaitOne(0) || twoClientRadar.Snapshot(launchedPid, null, false) is null)
                throw new InvalidOperationException($"Client {index + 1} was not fully ready");
            Console.WriteLine($"CLIENT_{index + 1}_READY pid={launchedPid} uuid={launchTemplate.Identity.SystemUuid}");
        }
        await Task.Delay(TimeSpan.FromSeconds(15), twoClientTimeout.Token);
        if (launched.Distinct().Count() != 2 || launched.Any(pid =>
            !twoClientService.IsAlive(pid) || twoClientRadar.Snapshot(pid, null, false) is null))
            throw new InvalidOperationException("Both clients did not remain alive with their radar sessions");
        Console.WriteLine($"TWO_CLIENTS_OK pids={string.Join(',', launched)} proxyLog={twoClientProxyLog}");
    }
    finally
    {
        foreach (var launchedPid in launched)
        {
            await twoClientRadar.StopAsync(launchedPid);
            twoClientService.Terminate(launchedPid);
        }
    }
    return;
}

var observe = args.Length == 2;
var useProxy = observe && args[1] is "--observe" or "--auto-login-local-proxy" or "--auto-login-local-proxy-saved" or "--observe-early-proxy-only" or "--auto-login-early-proxy-only";
var useRealProxy = observe && args[1] is "--observe-real-proxy" or "--auto-login-real-proxy" or "--auto-login-real-proxy-saved";
var boundBlackTemplate = observe && args[1] is "--auto-login-black-template" or "--auto-login-black-direct" or "--auto-login-black-isolated-data";
var autoLogin = observe && args[1] is "--auto-login" or "--auto-login-saved" or "--auto-login-real-proxy" or "--auto-login-real-proxy-saved" or "--auto-login-local-proxy" or "--auto-login-local-proxy-saved" or "--auto-login-early-proxy-only" or "--auto-login-black-template" or "--auto-login-black-direct" or "--auto-login-black-isolated-data";
var savedLogin = observe && args[1] is "--auto-login-saved" or "--auto-login-local-proxy-saved" or "--auto-login-real-proxy-saved" or "--auto-login-black-template" or "--auto-login-black-direct" or "--auto-login-black-isolated-data";
var autoProxy = observe && args[1] is "--auto-login-real-proxy" or "--auto-login-real-proxy-saved" or "--auto-login-black-template" or "--auto-login-black-direct" or "--auto-login-black-isolated-data";
if (useProxy || useRealProxy) Environment.SetEnvironmentVariable("PRICECHECK_EXPERIMENTAL_PROXY", "1");
var service = new ClientProcessService();
var profile = new CollectorProfile { LaunchFile = Path.GetFullPath(args[0]) };
var template = new LaunchTemplate { HardwareEnabled = !observe || args[1] is not ("--observe-early-proxy-only" or "--auto-login-early-proxy-only"), RotateEachLaunch = true };
if (autoLogin)
{
    if (boundBlackTemplate)
    {
        profile = (await new ProfileStore().LoadAsync()).Single(p => p.Name == "Black");
        profile.LaunchFile = Path.GetFullPath(args[0]);
        template = (await new LaunchTemplateStore().LoadAsync()).Single(t => t.Id == profile.LaunchTemplateId);
        if (args[1] == "--auto-login-black-direct") template.ProxyEnabled = false;
        if (Environment.GetEnvironmentVariable("PRICECHECK_TEST_ROTATE_TEMPLATE") == "1")
        {
            template.RotateEachLaunch = true;
            Console.WriteLine("Test launch uses a newly generated identity");
        }
        if (args[1] == "--auto-login-black-isolated-data")
        {
            var isolatedRoot = Path.GetFullPath(Path.Combine("workspace", "isolation-profile-black"));
            var isolatedLocal = Directory.CreateDirectory(Path.Combine(isolatedRoot, "Local")).FullName;
            var isolatedRoaming = Directory.CreateDirectory(Path.Combine(isolatedRoot, "Roaming")).FullName;
            Environment.SetEnvironmentVariable("LOCALAPPDATA", isolatedLocal);
            Environment.SetEnvironmentVariable("APPDATA", isolatedRoaming);
            Console.WriteLine($"Isolated user-data environment: {isolatedRoot}");
        }
        Console.WriteLine($"Using saved profile {profile.Name} and template {template.Name}; proxy enabled={template.ProxyEnabled}");
    }
    else if (savedLogin)
    {
        var saved = (await new ProfileStore().LoadAsync()).Single(p => p.Name == "Gamma");
        profile.LoginName = saved.LoginName;
        profile.LoginPassword = saved.LoginPassword;
        profile.LoginServerName = saved.LoginServerName;
        profile.CharacterSlot = saved.CharacterSlot;
        profile.AutoLoginEnabled = saved.AutoLoginEnabled;
    }
    else
    {
        Console.WriteLine("Enter test login and password on separate lines; console echo is disabled.");
        var inputHandle = ConsoleInput.GetStdHandle(-10);
        var hadConsole = ConsoleInput.GetConsoleMode(inputHandle, out var originalMode);
        if (hadConsole) ConsoleInput.SetConsoleMode(inputHandle, originalMode & ~4u);
        try
        {
            profile.LoginName = await Console.In.ReadLineAsync() ?? throw new EndOfStreamException();
            profile.LoginPassword = await Console.In.ReadLineAsync() ?? throw new EndOfStreamException();
            profile.LoginServerName = "Gamma";
            profile.CharacterSlot = 0;
            profile.AutoLoginEnabled = true;
        }
        finally { if (hadConsole) ConsoleInput.SetConsoleMode(inputHandle, originalMode); }
    }
}
string? proxyLog = null;
if (useProxy)
{
    using var reservation = new TcpListener(IPAddress.Loopback, 0);
    reservation.Start();
    var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
    reservation.Stop();
    proxyLog = Path.Combine(Path.GetTempPath(), $"pricecheck-login-proxy-{Environment.ProcessId}.txt");
    _ = Task.Run(() => ProxyServer.RunAsync(port, proxyLog));
    template.ProxyEnabled = true;
    template.ProxyHost = "127.0.0.1";
    template.ProxyPort = port;
    template.ProxyUser = "user";
    template.ProxyPassword = "pass";
}
if (useRealProxy)
{
    Console.WriteLine("Enter proxy host, port, user and password on four stdin lines; values are kept in memory only.");
    var inputHandle = ConsoleInput.GetStdHandle(-10);
    var hadConsole = ConsoleInput.GetConsoleMode(inputHandle, out var originalMode);
    if (hadConsole) ConsoleInput.SetConsoleMode(inputHandle, originalMode & ~4u);
    try
    {
    var host = await Console.In.ReadLineAsync() ?? throw new EndOfStreamException("Missing proxy host");
    var port = int.Parse(await Console.In.ReadLineAsync() ?? throw new EndOfStreamException("Missing proxy port"));
    var user = await Console.In.ReadLineAsync() ?? throw new EndOfStreamException("Missing proxy user");
    var password = await Console.In.ReadLineAsync() ?? throw new EndOfStreamException("Missing proxy password");
    using var probe = new TcpClient();
    await probe.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(10));
    var stream = probe.GetStream();
    var authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));
    var request = Encoding.ASCII.GetBytes("CONNECT 194.180.209.45:2108 HTTP/1.1\r\n" +
        "Host: 194.180.209.45:2108\r\nProxy-Authorization: Basic " + authorization + "\r\n\r\n");
    await stream.WriteAsync(request).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    var response = new byte[256];
    var count = await stream.ReadAsync(response).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    var status = Encoding.ASCII.GetString(response, 0, count).Split("\r\n", 2)[0];
    Console.WriteLine("Proxy preflight: " + status);
    if (!status.Contains(" 200 ", StringComparison.Ordinal))
        throw new InvalidOperationException("Proxy did not authorize CONNECT to the game login server");
    template.ProxyEnabled = true;
    template.ProxyHost = host;
    template.ProxyPort = port;
    template.ProxyUser = user;
    template.ProxyPassword = password;
    }
    finally
    {
        if (hadConsole) ConsoleInput.SetConsoleMode(inputHandle, originalMode);
    }
}
using var timeout = new CancellationTokenSource(observe ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(3));
if (autoLogin)
{
    Environment.SetEnvironmentVariable("PRICECHECK_TRACE_HARDWARE", "1");
    if (autoProxy) Environment.SetEnvironmentVariable("PRICECHECK_PROXY_DIAG", "1");
    else Environment.SetEnvironmentVariable("PRICECHECK_NETWORK_OBSERVE", "1");
}
var pid = 0;
try
{
    pid = await service.LaunchAndBindAsync(profile, template, timeout.Token);
    var rootOnlyProbe = string.Equals(profile.Name,
        Environment.GetEnvironmentVariable("PRICECHECK_EARLY_ROOT_ONLY_PROFILE"),
        StringComparison.OrdinalIgnoreCase);
    if (rootOnlyProbe)
    {
        await service.WaitForGameWindowAsync(pid, timeout.Token);
        await service.ActivateLateAgentAsync(pid, timeout.Token);
    }
    Console.WriteLine($"Agent ready in LU4 PID {pid}");
    if (autoLogin)
        Console.WriteLine($"TRACE={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceCheckCollector", "logs", $"hardware-trace-{pid}.csv")}");
    var adjacentProxy = Path.Combine(Path.GetDirectoryName(profile.LaunchFile)!, "version.dll");
    if (File.Exists(adjacentProxy)) throw new InvalidOperationException("version.dll is present beside the game");
    if (!observe) await Task.Delay(TimeSpan.FromSeconds(20), timeout.Token);
    if (!service.IsAlive(pid)) throw new InvalidOperationException("Client exited after agent readiness");
    using var ready = EventWaitHandle.OpenExisting($@"Local\PriceCheckAgentReady_{pid}");
    if (!ready.WaitOne(0)) throw new InvalidOperationException("Agent readiness was lost after removing GUI hook");
    Console.WriteLine("Client alive, agent pinned, no adjacent version.dll: OK");
    await service.WaitForGameWindowAsync(pid, timeout.Token);
    await using var radar = new RadarSessionManager();
    await radar.StartAsync(pid, null, false, timeout.Token);
    if (radar.Snapshot(pid, null, false) is null) throw new InvalidOperationException("Radar snapshot unavailable");
    Console.WriteLine("Driver-backed receive hook and radar snapshot: OK");
    var preloginGate = Environment.GetEnvironmentVariable("PRICECHECK_TEST_PRELOGIN_GATE");
    if (autoLogin && !string.IsNullOrWhiteSpace(preloginGate))
    {
        Console.WriteLine($"PRELOGIN_PAUSED pid={pid}");
        var gateDeadline = DateTime.UtcNow.AddSeconds(60);
        var gateOpened = false;
        while (!gateOpened && DateTime.UtcNow < gateDeadline)
        {
            if (File.Exists(preloginGate))
            {
                try
                {
                    var gateValue = File.ReadAllText(preloginGate).Trim();
                    if (gateValue == "abort") throw new OperationCanceledException("Prelogin probe aborted");
                    gateOpened = gateValue == "continue";
                }
                catch (IOException) { }
            }
            if (!service.IsAlive(pid)) throw new InvalidOperationException("Client exited before the prelogin gate opened");
            await Task.Delay(250, timeout.Token);
        }
        if (!gateOpened) throw new TimeoutException("Prelogin gate timed out");
    }
    if (autoLogin)
    {
        try { await new ClientLoginService().EnterAsync(pid, profile, timeout.Token); }
        catch when (autoProxy)
        {
            Console.WriteLine("Proxy login did not reach character selection. Holding test client for 25 seconds to inspect its screen.");
            await Task.Delay(TimeSpan.FromSeconds(25), timeout.Token);
            throw;
        }
        Console.WriteLine("Programmatic account, Gamma and character selection: OK");
        await Task.Delay(TimeSpan.FromSeconds(15), timeout.Token);
        var snapshot = radar.Snapshot(pid, null, false) ?? throw new InvalidOperationException("Radar stopped after entering world");
        Console.WriteLine($"Client and radar active after selection: player=({snapshot.PlayerX:N0},{snapshot.PlayerY:N0}), actors={snapshot.PositionedActors}");
        if (savedLogin)
        {
            Console.WriteLine($"SCREENSHOT_WINDOW_PID={pid}");
            var requestedHold = Environment.GetEnvironmentVariable("PRICECHECK_TEST_HOLD_SECONDS");
            var holdSeconds = int.TryParse(requestedHold, out var parsedHold) &&
                parsedHold is >= 0 and <= 120 ? parsedHold : useRealProxy ? 90 : 120;
            await Task.Delay(TimeSpan.FromSeconds(holdSeconds), timeout.Token);
        }
    }
    if (observe && !autoLogin)
    {
        Console.WriteLine($"READY_FOR_LOGIN PID={pid} TRACE={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceCheckCollector", "logs", $"hardware-trace-{pid}.csv")} PROXY_LOG={proxyLog}");
        Console.WriteLine("Press Enter to stop this test client after the login observation.");
        var stop = Console.In.ReadLineAsync();
        while (!stop.IsCompleted)
        {
            if (!service.IsAlive(pid)) throw new InvalidOperationException("Client exited during login observation");
            if (radar.Snapshot(pid, null, false) is null) throw new InvalidOperationException("Radar stopped during login observation");
            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
        }
    }
    await radar.StopAsync(pid);
}

finally
{
    if (pid != 0) service.Terminate(pid);
}

internal static class ConsoleInput
{
    [DllImport("kernel32.dll")] internal static extern nint GetStdHandle(int kind);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetConsoleMode(nint handle, out uint mode);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetConsoleMode(nint handle, uint mode);
}
