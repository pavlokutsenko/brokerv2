namespace PriceCheck.Collector.Runtime.Driver;

public sealed record ClientProcessStatus(int ProcessId, DateTimeOffset? StartedAtUtc, bool Active, int ExitStatus);
