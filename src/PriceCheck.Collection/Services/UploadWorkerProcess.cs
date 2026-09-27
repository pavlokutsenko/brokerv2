using System.Diagnostics;

namespace PriceCheck.Collector.Services;

public static class UploadWorkerProcess
{
    private static readonly object Gate = new();
    private static DateTimeOffset _lastStart = DateTimeOffset.MinValue;
    private static Process? _workerProcess;

    public static void EnsureRunning()
    {
        lock (Gate)
        {
            // Status and price envelopes can arrive every second. Keep one
            // helper for its whole lifetime instead of launching a new copy
            // every two seconds and leaving release files locked.
            if (_workerProcess is not null)
            {
                if (!_workerProcess.HasExited) return;
                _workerProcess.Dispose();
                _workerProcess = null;
            }
            if (DateTimeOffset.UtcNow - _lastStart < TimeSpan.FromSeconds(2)) return;
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return;
            _workerProcess = Process.Start(new ProcessStartInfo
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
