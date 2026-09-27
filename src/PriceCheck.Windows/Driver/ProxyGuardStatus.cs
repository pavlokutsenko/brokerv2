namespace PriceCheck.Collector.Runtime.Driver;

public sealed record ProxyGuardStatus(int ProcessId, int HostProcessId, int ListenerPort,
    uint Capabilities, bool Active, ulong BlockedConnections,
    uint BlockedProtocol = 0, uint BlockedLayer = 0, uint BlockedAddress = 0, uint BlockedPort = 0);
