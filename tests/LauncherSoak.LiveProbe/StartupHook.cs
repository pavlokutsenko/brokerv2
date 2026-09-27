using System.IO;
using System.Text.Json;
using System.Windows;
using PriceCheck.Collector.Services;
using PriceCheck.Contracts;
using PriceCheck.Launcher;
using PriceCheck.Windows;

// Test-only observer inside the actual portable Launcher apphost. It preserves
// the production App, MainWindow, timer, saved template, guards and ownership.
// A saved account is used in memory for the requested manual login; never saved.
public static class StartupHook
{
    private static readonly object Gate = new();
    private static string? _output;
    public static void Initialize()
    {
        _output = Environment.GetEnvironmentVariable("PRICECHECK_LAUNCHER_SOAK_LOG");
        if (_output is null) return;
        _ = Task.Run(ObserveAsync);
    }
    private static void Log(string phase, object? detail = null)
    {
        lock (Gate) File.AppendAllText(_output!, $"{DateTimeOffset.UtcNow:O} {phase} {JsonSerializer.Serialize(detail)}\n");
    }
    private static async Task ObserveAsync()
    {
        try
        {
            var id = Guid.Parse(Environment.GetEnvironmentVariable("PRICECHECK_LAUNCHER_SOAK_PROFILE")!);
            Log("REAL_LAUNCHER", new { Executable = Environment.ProcessPath, Base = AppContext.BaseDirectory, Profile = id });
            MainWindow? window = null;
            var readyBy = DateTimeOffset.UtcNow.AddMinutes(2);
            while (DateTimeOffset.UtcNow < readyBy && window is null)
            {
                if (Application.Current is { } app)
                    window = await app.Dispatcher.InvokeAsync(() => app.MainWindow as MainWindow);
                if (window is null) await Task.Delay(200);
            }
            if (window is null) throw new TimeoutException("The real Launcher window did not load.");
            await window.Dispatcher.InvokeAsync(() => window.Events.CollectionChanged += (_, args) =>
            {
                foreach (var value in args.NewItems ?? Array.Empty<object>()) Log("LAUNCHER_EVENT", value);
            });
            bool loginStarted = false;
            DateTimeOffset? worldSince = null;
            for (;;)
            {
                var snapshot = await window.Dispatcher.InvokeAsync(() =>
                {
                    var runtime = window.Runtimes.SingleOrDefault(value => value.Profile.Id == id);
                    return runtime is null ? null : new Observation(runtime.Session, runtime.IsBusy,
                        runtime.Protection, runtime.LaunchStatus, runtime.ClientFault, runtime.Profile.CharacterSlot,
                        runtime.Profile.AutoLoginEnabled, window.IsVisible);
                });
                if (snapshot is not null)
                {
                    var health = snapshot.Session is { } session ? ClientHealthProbe.Read(session) : null;
                    if (snapshot.Protection.WorldIdentityApplied && worldSince is null) worldSince = DateTimeOffset.UtcNow;
                    Log("LAUNCHER_STATE", new { State = snapshot, Health = health,
                        WorldSeconds = worldSince is { } since ? (DateTimeOffset.UtcNow - since).TotalSeconds : 0 });
                    if (!loginStarted && snapshot is { Busy: false, Session: not null, Protection.HardwareReady: true })
                    {
                        loginStarted = true;
                        if (!snapshot.AutoLogin)
                        {
                            var accountId = Guid.Parse(Environment.GetEnvironmentVariable("PRICECHECK_LAUNCHER_SOAK_ACCOUNT")!);
                            var account = (await new ProfileStore().LoadAsync()).Single(value => value.Id == accountId);
                            account.CharacterSlot = snapshot.Slot;
                            account.CharacterRotationEnabled = false;
                            Log("MANUAL_LOGIN_BEGIN", new { Pid = snapshot.Session.ProcessId, Slot = snapshot.Slot });
                            await new ClientLoginService().EnterAsync(snapshot.Session.ProcessId, account, CancellationToken.None);
                            Log("MANUAL_CHARACTER_SELECTED", new { Pid = snapshot.Session.ProcessId, Slot = snapshot.Slot });
                        }
                    }
                    if (snapshot.Protection.Failed) Log("PROTECTION_FAILURE", snapshot.Protection.Error);
                }
                await Task.Delay(2000);
            }
        }
        catch (Exception error) { Log("OBSERVER_ERROR", error.ToString()); }
    }
    private sealed record Observation(ClientSession? Session, bool Busy, ClientProtectionStatus Protection,
        string Status, string? Fault, int Slot, bool AutoLogin, bool Visible);
}
