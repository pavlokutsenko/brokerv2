using System.Windows;

namespace PriceCheck.Collector;

public partial class MainWindow
{
    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null || SelectedRuntime.IsBusy) return;
        var runtime = SelectedRuntime;
        runtime.IsBusy = true;
        runtime.Status = "Запускаю клиент…";
        try
        {
            var pid = await _processes.LaunchAndBindAsync(runtime.Profile, CancellationToken.None);
            runtime.ProcessId = pid;
            runtime.Status = "Клиент подключен";
            await SaveProfilesAsync();
            Log($"{runtime.Profile.Name}: запущен и привязан PID {pid}");
        }
        catch (Exception exception)
        {
            runtime.Status = "Ошибка запуска";
            Log(exception.Message);
            MessageBox.Show(exception.Message, "Не удалось запустить клиент", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { runtime.IsBusy = false; }
    }

    private async void Attach_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null) return;
        try
        {
            var pid = _processes.AttachNewestUnclaimed();
            SelectedRuntime.ProcessId = pid;
            SelectedRuntime.Profile.LastProcessId = pid;
            SelectedRuntime.Status = "Подключен вручную";
            await SaveProfilesAsync();
            Log($"{SelectedRuntime.Profile.Name}: привязан PID {pid}");
        }
        catch (Exception exception) { Log(exception.Message); }
    }

    private async void Detach_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRuntime is null) return;
        _processes.Release(SelectedRuntime.ProcessId);
        SelectedRuntime.ProcessId = null;
        SelectedRuntime.Profile.LastProcessId = null;
        SelectedRuntime.Status = "Отвязан";
        await SaveProfilesAsync();
        Log($"{SelectedRuntime.Profile.Name}: процесс отвязан");
    }
}

