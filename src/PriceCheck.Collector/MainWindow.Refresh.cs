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
                _processes.Release(pid);
                runtime.ProcessId = null;
                runtime.Profile.LastProcessId = null;
                runtime.Status = "Клиент завершён";
                await SaveProfilesAsync();
            }

            var radar = await _prototypeData.ReadRadarAsync(runtime.ProcessId);
            if (radar is not null) runtime.Radar = radar;
            runtime.Broker = await _prototypeData.ReadLatestBrokerAsync();
        }
        catch (IOException) { }
        catch (JsonException) { }
        finally { _refreshing = false; }
    }
}

