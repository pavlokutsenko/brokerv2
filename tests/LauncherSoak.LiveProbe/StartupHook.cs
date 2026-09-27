using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Diagnostics;
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
            if (Environment.GetEnvironmentVariable("PRICECHECK_LAUNCHER_SOAK_LOGIN_ACCOUNT") is { } loginAccount)
            {
                // Optional isolation experiment: change only the service argument,
                // never the persisted Launcher profile or its UI credentials.
                var account = (await new ProfileStore().LoadAsync()).Single(value => value.Id == Guid.Parse(loginAccount));
                var module = typeof(MainWindow).GetField("_launcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var field = module.GetType().GetField("_login", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var login = (Func<int, PriceCheck.Collector.Models.CollectorProfile, CancellationToken, Task>)field.GetValue(module)!;
                field.SetValue(module, (Func<int, PriceCheck.Collector.Models.CollectorProfile, CancellationToken, Task>)
                    ((pid, profile, token) =>
                    {
                        account.CharacterSlot = profile.CharacterSlot; account.CharacterRotationEnabled = false;
                        return login(pid, account, token);
                    }));
                Log("SAVED_ACCOUNT_ISOLATION", "Service argument only; profile/template/settings preserved.");
            }
            await window.Dispatcher.InvokeAsync(() => window.Events.CollectionChanged += (_, args) =>
            {
                foreach (var value in args.NewItems ?? Array.Empty<object>()) Log("LAUNCHER_EVENT", value);
            });
            bool loginStarted = false;
            DateTimeOffset? worldSince = null;
            Process? observedProcess = null;
            for (;;)
            {
                var snapshot = await window.Dispatcher.InvokeAsync(() =>
                {
                    var runtime = window.Runtimes.SingleOrDefault(value => value.Profile.Id == id);
                    var session = runtime?.Session ?? (runtime?.Profile.LastProcessId is int pid ? ClientProcessIdentity.Read(pid) : null);
                    return runtime is null ? null : new Observation(session, runtime.IsBusy,
                        runtime.Protection, runtime.LaunchStatus, runtime.ClientFault, runtime.Profile.CharacterSlot,
                        runtime.Profile.AutoLoginEnabled, window.IsVisible);
                });
                if (snapshot is not null)
                {
                    if (observedProcess is null && snapshot.Session is { } observed)
                    {
                        try
                        {
                            observedProcess = Process.GetProcessById(observed.ProcessId);
                            observedProcess.EnableRaisingEvents = true;
                            observedProcess.Exited += (_, _) => Log("PROCESS_EXIT", new { Pid = observed.ProcessId, Code = observedProcess.ExitCode });
                        }
                        catch (Exception error) { Log("EXIT_OBSERVER_UNAVAILABLE", error.Message); }
                    }
                    var health = snapshot.Session is { } session ? ClientHealthProbe.Read(session) : null;
                    if (snapshot.Protection.WorldIdentityApplied && worldSince is null) worldSince = DateTimeOffset.UtcNow;
                    Log("LAUNCHER_STATE", new { State = snapshot, Health = health, NativeLease = ReadLease(window, snapshot.Session?.ProcessId),
                        WorldSeconds = worldSince is { } since ? (DateTimeOffset.UtcNow - since).TotalSeconds : 0 });
                    if (!loginStarted && snapshot is { Busy: false, Session: not null, Protection.HardwareReady: true })
                    {
                        loginStarted = true;
                        if (!snapshot.AutoLogin && Environment.GetEnvironmentVariable("PRICECHECK_LAUNCHER_SOAK_ACCOUNT") is { } savedAccount)
                        {
                            var accountId = Guid.Parse(savedAccount);
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
    private static object? ReadLease(MainWindow window, int? pid)
    {
        if (pid is null) return null;
        try
        {
            object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(value);
            var module = Field(window, "_launcher")!;
            var processes = Field(module, "_processes")!;
            var guards = (System.Collections.IDictionary)Field(processes, "_guards")!;
            if (guards[pid.Value] is not { } guard) return null;
            var mapping = Field(guard, "_mapping")!;
            var name = (string)mapping.GetType().GetProperty("Name")!.GetValue(mapping)!;
            using var file = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            using var view = file.CreateViewAccessor(0, 104, MemoryMappedFileAccess.Read);
            return new { Error = view.ReadInt32(12), HeartbeatAge = Environment.TickCount64 - view.ReadInt64(16),
                Flags = view.ReadInt32(24), WorldCount = view.ReadInt32(28), AgentAge = Environment.TickCount64 - view.ReadInt64(72),
                Required = view.ReadInt32(80), Controller = view.ReadInt32(84) };
        }
        catch (Exception error) { return new { ReadError = error.Message }; }
    }
}
