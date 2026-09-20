using System.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null || SelectedRuntime.IsBusy) return;
        var runtime = SelectedRuntime;
        if (runtime.ProcessId is int current && _processes.IsAlive(current))
        {
            Log($"{runtime.Profile.Name}: клиент уже запущен");
            return;
        }
        runtime.IsBusy = true;
        runtime.Status = "Запускаю клиент…";
        int? pid = null;
        try
        {
            pid = await _processes.LaunchAndBindAsync(runtime.Profile, CancellationToken.None);
            runtime.ProcessId = pid;
            runtime.Status = "Жду завершения Active Anticheat…";
            await _processes.WaitForGameWindowAsync(pid.Value, CancellationToken.None);
            runtime.Status = "Устанавливаю packet radar…";
            await _radarSessions.StartAsync(pid.Value, CancellationToken.None);
            runtime.Status = "Радар готов · можно входить";
            await SaveProfilesAsync();
            Log($"{runtime.Profile.Name}: PID {pid}, receive-hook установлен до входа");
        }
        catch (Exception exception)
        {
            runtime.Status = "Ошибка запуска";
            if (pid is int failedPid)
            {
                await _radarSessions.StopAsync(failedPid);
                _processes.Terminate(failedPid);
                runtime.ProcessId = null;
                runtime.Profile.LastProcessId = null;
            }
            Log(exception.Message);
            MessageBox.Show(exception.Message, "Не удалось запустить клиент", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { runtime.IsBusy = false; }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null || SelectedRuntime.IsBusy) return;
        var runtime = SelectedRuntime;
        runtime.IsBusy = true;
        runtime.Status = "Останавливаю…";
        var pid = runtime.ProcessId;
        if (pid is int value)
        {
            await _radarSessions.StopAsync(value);
            _processes.Terminate(value);
        }
        runtime.ProcessId = null;
        runtime.Profile.LastProcessId = null;
        runtime.Radar = null;
        runtime.Status = "Остановлен";
        await SaveProfilesAsync();
        Log($"{runtime.Profile.Name}: hook снят, клиент завершён");
        runtime.IsBusy = false;
    }
}
