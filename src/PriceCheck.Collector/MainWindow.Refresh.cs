namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async Task RefreshSelectedAsync()
    {
        if (_refreshing || SelectedRuntime is null) return;
        _refreshing = true;
        try
        {
            var runtime = SelectedRuntime;
            if (runtime.ProcessId is int pid && !_processes.IsAlive(pid))
            {
                await _radarSessions.StopAsync(pid);
                _processes.Release(pid);
                runtime.ProcessId = null;
                runtime.Profile.LastProcessId = null;
                runtime.Status = "Клиент завершён";
                await SaveProfilesAsync();
            }

            var radar = runtime.ProcessId is int livePid ? _radarSessions.Snapshot(livePid) : null;
            if (radar is not null) runtime.Radar = radar;
            // Broker values must belong to this profile's live session. Never
            // surface old research JSON as if it were current market state.
            runtime.Broker = null;
        }
        catch (IOException) { }
        catch (JsonException) { }
        catch (Exception exception)
        {
            var runtime = SelectedRuntime;
            if (runtime?.ProcessId is int pid)
            {
                await _radarSessions.StopAsync(pid);
                _processes.Terminate(pid);
                runtime.ProcessId = null;
                runtime.Profile.LastProcessId = null;
                runtime.Status = $"Радар остановлен: {exception.GetBaseException().Message}";
                Log(runtime.Status);
                await SaveProfilesAsync();
            }
        }
        finally { _refreshing = false; }
    }
}
