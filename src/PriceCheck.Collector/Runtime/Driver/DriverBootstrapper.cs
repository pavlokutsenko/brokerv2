using System.ComponentModel;
using System.Diagnostics;

namespace PriceCheck.Collector.Runtime.Driver;

public sealed class DriverBootstrapper
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(2);

    public void EnsureReady()
    {
        if (IsDeviceReady()) return;

        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "DriverRuntime");
        var loaderPath = Path.Combine(runtimeDirectory, "load-driver.ps1");
        if (!File.Exists(loaderPath))
            throw new FileNotFoundException("В сборке отсутствует загрузчик LU4Memory", loaderPath);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheck Collector",
            "logs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, "driver-bootstrap.log");

        using var process = StartElevatedLoader(loaderPath, runtimeDirectory, logPath);
        if (!process.WaitForExit((int)LoadTimeout.TotalMilliseconds))
        {
            TryTerminate(process);
            throw new TimeoutException("Загрузка LU4Memory не завершилась за 2 минуты");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(ReadFailure(logPath, process.ExitCode));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (IsDeviceReady()) return;
            Thread.Sleep(200);
        }
        throw new InvalidOperationException("LU4Memory запущен, но устройство \\\\.\\LU4Memory недоступно");
    }

    private static Process StartElevatedLoader(string loaderPath, string workingDirectory, string logPath)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{loaderPath}\" -LogPath \"{logPath}\""
        };
        try
        {
            return Process.Start(start) ?? throw new InvalidOperationException("Windows не запустил загрузчик LU4Memory");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Для загрузки LU4Memory нужно подтвердить запрос UAC", exception);
        }
    }

    private static bool IsDeviceReady()
    {
        try
        {
            using var device = new Lu4Device();
            var imageBase = device.GetProcessBase(Environment.ProcessId);
            var signature = device.Read(Environment.ProcessId, imageBase, 2);
            return signature is [(byte)'M', (byte)'Z'];
        }
        catch
        {
            return false;
        }
    }

    private static string ReadFailure(string logPath, int exitCode)
    {
        if (!File.Exists(logPath)) return $"Загрузчик LU4Memory завершился с кодом {exitCode}";
        var lines = File.ReadAllLines(logPath).Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(8);
        return $"Не удалось загрузить LU4Memory (код {exitCode}):\n{string.Join(Environment.NewLine, lines)}";
    }

    private static void TryTerminate(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { }
    }
}
