using System.ComponentModel;
using System.Diagnostics;

namespace PriceCheck.Collector.Runtime.Driver;

public sealed class DriverBootstrapper
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(PriceCheck.Windows.LaunchTimeouts.DriverLoadSeconds);

    public void EnsureReady()
    {
        var forceReload = IsDeviceReady();
        if (forceReload && IsProxyCapable()) return;

        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "DriverRuntime");
        var loaderPath = Path.Combine(runtimeDirectory, "load-driver.ps1");
        if (!File.Exists(loaderPath))
            throw new FileNotFoundException("LU4Memory loader is missing from the package.", loaderPath);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheckCollector",
            "logs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, "driver-bootstrap.log");

        using var process = StartElevatedLoader(loaderPath, runtimeDirectory, logPath, forceReload);
        if (!process.WaitForExit((int)LoadTimeout.TotalMilliseconds))
        {
            TryTerminate(process);
            throw new TimeoutException("LU4Memory loading did not finish within 5 minutes.");
        }

        if (process.ExitCode != 0)
        {
            var processError = ReadProcessError(process);
            if (!string.IsNullOrWhiteSpace(processError))
                File.WriteAllText(logPath + ".process.log", processError);
            throw new InvalidOperationException(ReadFailure(logPath, process.ExitCode, processError));
        }

        var deadline = DateTime.UtcNow.AddSeconds(PriceCheck.Windows.LaunchTimeouts.DriverReadySeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (IsDeviceReady() && IsProxyCapable()) return;
            Thread.Sleep(200);
        }
            throw new InvalidOperationException("LU4Memory is running, but WFP proxy support is unavailable.");
    }

    private static Process StartElevatedLoader(string loaderPath, string workingDirectory, string logPath, bool forceReload)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = workingDirectory,
            UseShellExecute = !IsAdministrator(),
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (start.UseShellExecute) start.Verb = "runas";
        else
        {
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
        }

        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(loaderPath);
        start.ArgumentList.Add("-LogPath");
        start.ArgumentList.Add(logPath);
        if (forceReload) start.ArgumentList.Add("-ForceReload");
        try
        {
        return Process.Start(start) ?? throw new InvalidOperationException("Windows did not start the LU4Memory loader.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Approve the UAC prompt to load LU4Memory.", exception);
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
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

    private static bool IsProxyCapable()
    {
        try
        {
            using var device = new Lu4Device();
            return device.QueryProxyGuard().Capabilities == 31;
        }
        catch { return false; }
    }

    private static string ReadProcessError(Process process)
    {
        if (process.StartInfo.UseShellExecute) return string.Empty;
        return string.Join(Environment.NewLine,
            process.StandardError.ReadToEnd(),
            process.StandardOutput.ReadToEnd()).Trim();
    }

    private static string ReadFailure(string logPath, int exitCode, string processError)
    {
        if (!File.Exists(logPath))
            return string.IsNullOrWhiteSpace(processError)
            ? $"LU4Memory loader exited with code {exitCode}"
            : $"LU4Memory loader exited with code {exitCode}:\n{processError}";
        var lines = File.ReadAllLines(logPath).Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(8);
        return $"Could not load LU4Memory (code {exitCode}):\n{string.Join(Environment.NewLine, lines)}";
    }

    private static void TryTerminate(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { }
    }
}
