namespace PriceCheck.Collector.Services;

public static class BrokerRuntimeIsolation
{
    private static readonly object Sync = new();

    public static string WorkerFor(PriceCheck.Contracts.ClientSession session)
    {
        var pid=session.ProcessId;
        var source = Path.Combine(AppContext.BaseDirectory, "BrokerRuntime");
        var sourceWorker = Path.Combine(source, "BrokerWorker.exe");
        if (!File.Exists(sourceWorker))
            throw new FileNotFoundException("BrokerWorker is missing from the collector package.", sourceWorker);

        var sourceStamp = Stamp(sourceWorker);
        // Versioned directories retain old cleanup journals. Never delete a
        // runtime that a live worker may still own during a package update.
        var target = Path.Combine(CycleQueue.Root, "runtime-sessions", $"{pid}-{session.StartedAtUtc.UtcTicks}", sourceStamp, "BrokerRuntime");
        var targetWorker = Path.Combine(target, "BrokerWorker.exe");
        var marker = Path.Combine(target, ".source-stamp");

        lock (Sync)
        {
            var currentStamp = File.Exists(marker) ? File.ReadAllText(marker) : "";
            if (!File.Exists(targetWorker) || !string.Equals(currentStamp, sourceStamp, StringComparison.Ordinal))
            {
                CopyCleanRuntime(source, target);
                File.WriteAllText(marker, sourceStamp);
            }
        }

        return targetWorker;
    }

    private static string Stamp(string worker) =>
        $"{new FileInfo(worker).Length}-{File.GetLastWriteTimeUtc(worker).Ticks}";

    private static void CopyCleanRuntime(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            if (IsRuntimeOutput(relative)) continue;
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (IsRuntimeOutput(relative)) continue;
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static bool IsRuntimeOutput(string relative)
    {
        var normalized = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var buildPrefix = Path.Combine("_internal", "build");
        if (normalized.Equals(buildPrefix, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(buildPrefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;

        var diagnosticsPrefix = Path.Combine("_internal", "diagnostics") + Path.DirectorySeparatorChar;
        return normalized.StartsWith(diagnosticsPrefix, StringComparison.OrdinalIgnoreCase) &&
               Path.GetFileName(normalized).StartsWith("latest", StringComparison.OrdinalIgnoreCase) &&
               normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }
}
