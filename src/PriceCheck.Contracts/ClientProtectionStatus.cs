namespace PriceCheck.Contracts;

public sealed record ClientProtectionStatus(bool HardwareReady, bool WorldIdentityApplied,
    bool ProxyReady, long ProxiedConnections, long SentBytes, long ReceivedBytes,
    string IdentityTag, string? Error, ulong BlockedConnections = 0)
{
    public static ClientProtectionStatus Pending { get; } = new(false, false, false, 0, 0, 0, "", null);
    public bool Failed => Error is not null;
    public string HardwareLabel => Failed ? "HWID · ОШИБКА" : WorldIdentityApplied
        ? $"HWID · применён в мире · {IdentityTag}" : HardwareReady
        ? $"HWID · проверен, ожидание входа · {IdentityTag}" : "HWID · не проверен";
    public string ProxyLabel => Failed ? "ПРОКСИ · ОШИБКА" : ProxyReady
        ? $"ПРОКСИ · защита активна · {ProxiedConnections} CONNECT · ↑ {SentBytes:N0} / ↓ {ReceivedBytes:N0} Б · блокировано {BlockedConnections}"
        : "ПРОКСИ · не проверен";
    public string Detail => Error ?? "Трафик игровых процессов: IPv4/TCP через HTTP CONNECT. UDP/IPv6 и прямые подключения блокируются.";
}
