using PriceCheck.Contracts;

namespace PriceCheck.Windows;

// An open file handle gives exclusive ownership across processes without thread
// affinity. Windows releases it on a process crash; the empty file is not a lock.
public static class ClientReaderLease
{
    public static FileStream Acquire(ClientSession session, string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PriceCheckCollector", "collection", "reader-owners");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{session.ProcessId}-{session.StartedAtUtc.UtcTicks}.lock");
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) when ((e.HResult & 0xFFFF) is 32 or 33)
        { throw new InvalidOperationException($"Client PID {session.ProcessId} already has a reader in another collector. Disconnect it there first.", e); }
    }
}
