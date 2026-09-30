using System.Collections;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PriceCheck.Collector;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Contracts;
using PriceCheck.Launching;
using PriceCheck.Windows;

// Test-only, read-only observer of the actual app and its existing launch leases.
// No startup, login, template, recovery, reader or guard behavior is replaced.
public static class StartupHook
{
    private static string? _output;
    public static void Initialize()
    {
        _output = Environment.GetEnvironmentVariable("PRICECHECK_IDENTITY_OBSERVER_LOG");
        if (_output is not null) _ = Task.Run(ObserveAsync);
    }
    private static void Log(string phase, object value) => File.AppendAllText(_output!,
        $"{DateTimeOffset.UtcNow:O} {phase} {JsonSerializer.Serialize(value)}\n");
    private static object? Field(object owner, string name) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner);

    private static async Task ObserveAsync()
    {
        try
        {
            var profileId = Guid.Parse(Environment.GetEnvironmentVariable("PRICECHECK_IDENTITY_OBSERVER_PROFILE")!);
            var accountCount = int.Parse(Environment.GetEnvironmentVariable("PRICECHECK_IDENTITY_OBSERVER_ACCOUNTS") ?? "3");
            if (accountCount is < 1 or > 21) throw new ArgumentException("Invalid expected account count.");
            Log("APP", new { Pid = Environment.ProcessId, Executable = Environment.ProcessPath, ProfileId = profileId });
            MainWindow? window = null;
            var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
            while (window is null && DateTimeOffset.UtcNow < deadline)
            {
                if (Application.Current is { } app)
                    window = await app.Dispatcher.InvokeAsync(() => app.MainWindow as MainWindow);
                if (window is null) await Task.Delay(200);
            }
            if (window is null) throw new TimeoutException("Collector window did not load.");
            await window.Dispatcher.InvokeAsync(() => window.Events.CollectionChanged += (_, change) =>
            {
                foreach (var item in change.NewItems ?? Array.Empty<object>()) Log("EVENT", item);
            });
            DateTimeOffset? stableSince = null;
            deadline = DateTimeOffset.UtcNow.AddMinutes(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var states = await window.Dispatcher.InvokeAsync(() => Capture(window, profileId));
                var health = states.Select(state => state.Session is { } session ? ClientHealthProbe.Read(session) : null).ToArray();
                var ready = states.Length == accountCount && states.All(state => !state.Busy && state.Reader && state.Session is not null &&
                    state.Protection.HardwareReady && state.Protection.WorldIdentityApplied && !state.Protection.Failed &&
                    (!state.Protection.ProxyRequired || state.Protection.ProxyReady) && state.TemplateMatches &&
                    state.Lease is { Error: 0, Flags: 7, Controller: 1, PidMatches: true, BirthMatches: true, WorldMatches: true } lease &&
                    lease.AgentAge is >= 0 and < 15000 && lease.ControllerAge is >= 0 and < 15000) &&
                    states.Select(state => state.Session!.ProcessId).Distinct().Count() == accountCount &&
                    states.Select(state => state.Protection.IdentityTag).Distinct().Count() == accountCount &&
                    health.All(value => value is { Current: true, Responsive: true, FatalWindow: false });
                Log("STATE", new { States = states, Health = health, Ready = ready, Driver = DriverIdentityService.Status });
                stableSince = ready ? stableSince ?? DateTimeOffset.UtcNow : null;
                if (stableSince is { } since && (DateTimeOffset.UtcNow - since).TotalSeconds >= 60)
                {
                    await window.Dispatcher.InvokeAsync(() => Render(window));
                    Log("PASS", new { Seconds = (DateTimeOffset.UtcNow - since).TotalSeconds, States = states, Health = health });
                    return;
                }
                await Task.Delay(2000);
            }
            Log("TIMEOUT", "The configured stable, distinct, protected world sessions were not confirmed.");
        }
        catch (Exception error) { Log("OBSERVER_ERROR", error.ToString()); }
    }

    private static State[] Capture(MainWindow window, Guid rootId)
    {
        var root = window.Runtimes.FirstOrDefault(value => value.Profile.Id == rootId);
        if (root is null) return [];
        var accounts = (IReadOnlyList<ProfileRuntime>)typeof(MainWindow).GetMethod("MarketAccounts",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [root])!;
        var launcher = (LaunchModule)Field(window, "_launcher")!;
        var owned = (Dictionary<Guid, ClientSession>)Field(launcher, "_owned")!;
        return accounts.Select(runtime =>
        {
            var session = runtime.Session ?? owned.GetValueOrDefault(runtime.Profile.Id);
            var protection = launcher.Protection(runtime.Profile.Id);
            var template = window.LaunchTemplates.FirstOrDefault(value => value.Id == runtime.Profile.LaunchTemplateId);
            var resolver = typeof(ClientLaunchConfiguration).Assembly.GetType("PriceCheck.Collector.Services.WorldIdentityConfiguration")!
                .GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)!;
            var world = template is null ? [] : Convert.FromHexString((string)resolver.Invoke(null,
                [runtime.Profile.Id, template.Identity.WorldIdentitySeed])!);
            var tag = world.Length == 16 ? Convert.ToHexString(SHA256.HashData(world))[..12] : "";
            return new State(runtime.Profile.Id, runtime.Profile.LaunchTemplateId, session, runtime.IsBusy,
                runtime.ReaderAttached, runtime.LaunchStatus, runtime.ClientFault, protection,
                protection.IdentityTag == tag && tag.Length > 0, ReadLease(launcher, session, world));
        }).ToArray();
    }

    private sealed record State(Guid Account, Guid Template, ClientSession? Session, bool Busy, bool Reader,
        string Status, string? Fault, ClientProtectionStatus Protection, bool TemplateMatches, Lease? Lease);
    private sealed record Lease(int Flags, int Error, int Controller, long AgentAge, long ControllerAge,
        bool PidMatches, bool BirthMatches, bool WorldMatches, int WorldCount);
    private static Lease? ReadLease(LaunchModule launcher, ClientSession? session, byte[] world)
    {
        if (session is null) return null;
        var processes = Field(launcher, "_processes")!;
        var guards = (IDictionary)Field(processes, "_guards")!;
        if (guards[session.ProcessId] is not { } guard) return null;
        var mapping = Field(guard, "_mapping")!;
        var name = (string)mapping.GetType().GetProperty("Name")!.GetValue(mapping)!;
        using var file = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
        using var view = file.CreateViewAccessor(0, 104, MemoryMappedFileAccess.Read);
        var applied = new byte[16]; view.ReadArray(32, applied, 0, applied.Length);
        return new(view.ReadInt32(24), view.ReadInt32(12), view.ReadInt32(84), Environment.TickCount64 - view.ReadInt64(72),
            Environment.TickCount64 - view.ReadInt64(16), view.ReadInt32(8) == session.ProcessId,
            view.ReadInt64(64) == session.StartedAtUtc.UtcDateTime.ToFileTimeUtc(), applied.SequenceEqual(world), view.ReadInt32(28));
    }
    private static void Render(MainWindow window)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.ChangeExtension(_output!, ".png")); encoder.Save(file);
    }
}
