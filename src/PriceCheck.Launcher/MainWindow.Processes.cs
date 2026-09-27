using System.ComponentModel;
using System.Windows;
using PriceCheck.Contracts;
using PriceCheck.Launching;
using PriceCheck.Windows;

namespace PriceCheck.Launcher;

public partial class MainWindow
{
    private async void Launch_Click(object sender, RoutedEventArgs e) => await LaunchAsync(SelectedRuntime);
    private async Task LaunchAsync(LaunchRuntime? runtime)
    {
        if (runtime is null || !_loaded || runtime.IsBusy || _closing) return;
        if (runtime.Session is { } current && ClientProcessIdentity.IsCurrent(current)) { Log("Client is already running"); return; }
        runtime.IsBusy = true;
        var template = LaunchTemplates.FirstOrDefault(value => value.Id == runtime.Profile.LaunchTemplateId && value.Id != Guid.Empty);
        try
        {
            if (Runtimes.Count(value => value.Session is { } session && ClientProcessIdentity.IsCurrent(session)) >= 4)
                throw new InvalidOperationException("Одновременно поддерживаются до четырёх игровых клиентов.");
            CharacterRotationSchedule.Validate(runtime.Profile);
            _recovery.Forget(runtime.Profile.Id); runtime.ClientFault = null;
            if (runtime.Profile.CharacterRotationEnabled) runtime.Profile.CharacterSlot = 0;
            SetSession(runtime, await _launcher.LaunchAsync(runtime.Profile, template, status => runtime.LaunchStatus = status, CancellationToken.None));
            _rotation.Started(runtime.Profile, runtime.Session!, DateTimeOffset.UtcNow);
            Log($"{runtime.Profile.Name}: {runtime.ProcessLabel} · ready");
        }
        catch (Exception exception) { runtime.LaunchStatus = "Launch failed"; ClearExited(runtime); Error(exception); }
        finally
        {
            runtime.IsBusy = false;
            try { await SaveProfilesAsync(); await SaveRotatedTemplateAsync(template); }
            catch (Exception exception) { Error(exception); }
        }
    }
    private static void SetSession(LaunchRuntime runtime, ClientSession session)
    {
        runtime.Session = session; runtime.Profile.LastProcessId = session.ProcessId;
        runtime.Profile.LastProcessStartUtc = session.StartedAtUtc; runtime.ClientFault = null;
    }
    private void ClearExited(LaunchRuntime runtime)
    {
        if (runtime.Session is { } session && ClientProcessIdentity.IsCurrent(session)) return;
        _launcher.ReleaseExited(runtime.Profile.Id);
        runtime.Session = null; runtime.Profile.LastProcessId = null; runtime.Profile.LastProcessStartUtc = null;
    }
    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is not { IsBusy: false } runtime) return;
        try { await StopAsync(runtime); } catch (Exception exception) { Error(exception); }
    }
    private async Task StopAsync(LaunchRuntime runtime)
    {
        runtime.IsBusy = true;
        try
        {
            _recovery.Forget(runtime.Profile.Id); _rotation.Forget(runtime.Profile.Id);
            _launcher.Stop(runtime.Profile.Id); runtime.Session = null;
            runtime.Profile.LastProcessId = null; runtime.Profile.LastProcessStartUtc = null;
            runtime.LaunchStatus = "Stopped"; await SaveProfilesAsync();
        }
        finally { runtime.IsBusy = false; }
    }
    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeReady) return;
        e.Cancel = true;
        if (_closing) return;
        if (Runtimes.Any(value => value.IsBusy)) { Log("Дождитесь завершения текущей операции."); return; }
        _closing = true; _timer.Stop();
        try
        {
            _launcher.StopOwnedClients(); await SaveProfilesAsync();
            _closeReady = true; _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        catch (Exception exception) { _closing = false; _timer.Start(); Error(exception); }
    }
}
