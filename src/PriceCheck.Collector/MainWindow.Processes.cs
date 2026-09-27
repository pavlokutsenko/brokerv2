using System.ComponentModel;
using System.Windows;
using PriceCheck.Collector.Models;
using PriceCheck.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async void Launch_Click(object sender, RoutedEventArgs e) => await LaunchProfileAsync(SelectedRuntime);

    private async Task LaunchProfileAsync(ProfileRuntime? runtime, bool resumeCharacter = false)
    {
        if (runtime is null || runtime.IsBusy || _closing) return;
        if (runtime.Session is { } current && ClientProcessIdentity.IsCurrent(current))
        {
            Log($"{runtime.Profile.Name}: client is already running");
            return;
        }
        runtime.IsBusy = true;
        var template = LaunchTemplates.FirstOrDefault(value => value.Id == runtime.Profile.LaunchTemplateId && value.Id != Guid.Empty);
        try
        {
            if (Runtimes.Count(other => other.Session is { } active && ClientProcessIdentity.IsCurrent(active)) >= 4)
                throw new InvalidOperationException("Одновременно поддерживаются от 1 до 4 игровых клиентов.");
            if (Runtimes.Any(other => other != runtime && other.Session is not null &&
                ClientProcessIdentity.IsCurrent(other.Session) &&
                other.Profile.Name.Equals(runtime.Profile.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("На этом рынке уже работает другой профиль.");
            PriceCheck.Launching.CharacterRotationSchedule.Validate(runtime.Profile);
            if (runtime.Profile.CharacterRotationEnabled && !resumeCharacter)
                runtime.Profile.CharacterSlot = 0;
            if (runtime.ReaderAttached) await _collection.DetachAsync(runtime);
            runtime.Session = await _launcher.LaunchAsync(runtime.Profile, template,
                status => { runtime.LaunchStatus = status; runtime.Protection = _launcher.Protection(runtime.Profile.Id); }, CancellationToken.None,
                CaptureReaderBeforeLogin(runtime));
            _characterRotation.Started(runtime.Profile,runtime.Session,DateTimeOffset.UtcNow);
            OnPropertyChanged(nameof(CharacterOptions));
            runtime.Profile.LastProcessId = runtime.Session.ProcessId;
            runtime.Profile.LastProcessStartUtc = runtime.Session.StartedAtUtc;
            await SaveProfilesAsync();
            Log($"{runtime.Profile.Name}: PID {runtime.ProcessId} · launcher ready; reader {(runtime.ReaderAttached?"connected":"disconnected")}");
        }
        catch (Exception exception)
        {
            runtime.LaunchStatus = "Launch failed";
            if (runtime.Session is null || !ClientProcessIdentity.IsCurrent(runtime.Session))
            {
                runtime.Session = null;
                runtime.Profile.LastProcessId = null;
                runtime.Profile.LastProcessStartUtc = null;
            }
            runtime.Protection = _launcher.Protection(runtime.Profile.Id);
            ShowModuleError(runtime, exception);
        }
        finally
        {
            runtime.Protection = _launcher.Protection(runtime.Profile.Id);
            runtime.IsBusy = false;
            if (template?.RotateEachLaunch == true)
            {
                await SaveTemplatesAsync();
                TemplatesView.RefreshAfterLaunch(LaunchTemplates.Where(value => value.Id != Guid.Empty));
            }
            RefreshClientList();
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is { IsBusy: false } runtime) await StopProfileAsync(runtime);
    }

    private async Task<bool> StopProfileAsync(ProfileRuntime runtime)
    {
        _clientRecovery.Forget(runtime.Profile.Id);
        _characterRotation.Forget(runtime.Profile.Id);
        runtime.IsBusy = true;
        try
        {
            await _collection.DetachAsync(runtime);
            if (runtime.Session is { } session && !_launcher.Owns(runtime.Profile.Id, session) && ClientProcessIdentity.IsCurrent(session))
            {
                runtime.LaunchStatus = "Client managed elsewhere · reader disconnected";
                Log($"{runtime.Profile.Name}: external client left open");
            }
            else
            {
                _launcher.Stop(runtime.Profile.Id);
                runtime.LaunchStatus = "Stopped";
            }
            runtime.Session = null;
            runtime.Profile.LastProcessId = null;
            runtime.Profile.LastProcessStartUtc = null;
            await SaveProfilesAsync();
            return true;
        }
        catch (Exception exception) { ShowModuleError(runtime, exception); return false; }
        finally { runtime.Protection = _launcher.Protection(runtime.Profile.Id); runtime.IsBusy = false; }
    }

    private void ShowModuleError(ProfileRuntime runtime, Exception exception)
    {
        _journal.Append(new(DateTimeOffset.UtcNow, "ERROR", runtime.Profile.Name, runtime.Profile.Name, "",
            PriceCheck.Collector.Services.CollectorJournal.Redact(exception.ToString())));
        Log($"ERROR {runtime.Profile.Name}: {exception.GetBaseException().Message}");
        MessageBox.Show(this, exception.GetBaseException().Message, "PriceCheck Collector", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeReady) return;
        e.Cancel = true;
        if (_closing) return;
        if (Runtimes.Any(runtime => runtime.IsBusy))
        {
            Log("Wait for the current module operation before closing.");
            return;
        }
        _closing = true;
        _refreshTimer.Stop();
        try
        {
            var refreshDeadline=DateTimeOffset.UtcNow.AddMinutes(5);
            while(_refreshing)
            {
                if(DateTimeOffset.UtcNow>=refreshDeadline) throw new TimeoutException("Module refresh has not finished before closing.");
                await Task.Delay(50);
            }
            foreach (var runtime in Runtimes) await _collection.DetachAsync(runtime);
            _launcher.StopOwnedClients();
            await SaveProfilesAsync();
            await _journal.FlushAsync();
            _closeReady = true;
            // Cleanup may complete synchronously for an idle window. Close
            // only after WPF has unwound this canceled Closing notification.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        catch (Exception exception)
        {
            Log($"Close postponed: {exception}");
            MessageBox.Show(this, $"Reader cleanup is not finished: {exception.GetBaseException().Message}\nClients remain open. Try closing again after the current operation completes.", "PriceCheck Collector");
            _closing = false;
            _refreshTimer.Start();
        }
    }
}
