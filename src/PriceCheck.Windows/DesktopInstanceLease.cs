namespace PriceCheck.Windows;

// The desktop UI is the only configuration writer. Upload workers do not claim this lease.
public sealed class DesktopInstanceLease : IDisposable
{
    private readonly Mutex _mutex;
    public bool Acquired { get; }
    public DesktopInstanceLease(string product)
    {
        _mutex = new Mutex(false, $@"Local\PriceCheck.{product}.Desktop");
        try { Acquired = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { Acquired = true; }
    }
    public void Dispose()
    {
        if (Acquired) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
