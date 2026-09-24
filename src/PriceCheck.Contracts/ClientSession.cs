namespace PriceCheck.Contracts;

// PID alone is not an identity: Windows can reuse it after a client exits.
public sealed record ClientSession(int ProcessId, DateTimeOffset StartedAtUtc)
{
    public string Label => $"PID {ProcessId} · {StartedAtUtc.ToLocalTime():HH:mm:ss}";
}
