using System.Diagnostics;

namespace PriceCheck.Collector.Services;

public static class UploadWorkerProcess
{
    private static readonly object Gate = new();
    private static DateTimeOffset _lastStart = DateTimeOffset.MinValue;

    public static void EnsureRunning()
    {
        lock (Gate)
        {
            if (DateTimeOffset.UtcNow - _lastStart < TimeSpan.FromSeconds(2)) return;
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "--upload-worker", ServerUploadOutbox.DirectoryPath }
            });
            _lastStart = DateTimeOffset.UtcNow;
        }
    }
}
