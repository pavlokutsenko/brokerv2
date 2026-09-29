namespace PriceCheck.Contracts;

public sealed record ClientProtectionStatus(bool HardwareReady, bool WorldIdentityApplied,
    bool ProxyReady, long ProxiedConnections, long SentBytes, long ReceivedBytes,
    string IdentityTag, string? Error, ulong BlockedConnections = 0, bool ProxyRequired = true)
{
    public static ClientProtectionStatus Pending { get; } = new(false, false, false, 0, 0, 0, "", null);
    public bool Failed => Error is not null;
    public string HardwareLabel => Failed ? (Error!.StartsWith("HWID:", StringComparison.Ordinal)
        ? "HWID · ОШИБКА" : "HWID · проверка прервана") : WorldIdentityApplied
        ? $"HWID · применён в игровом мире · {IdentityTag}" : HardwareReady
        ? $"HWID · проверен, ожидание входа · {IdentityTag}" : "HWID · не проверен";
    public string ProxyLabel => !ProxyRequired ? "ПРОКСИ · отключён в шаблоне" : Failed ? "ПРОКСИ · ОШИБКА" : ProxyReady
        ? $"ПРОКСИ · активен · {ProxiedConnections} CONNECT · ↑ {SentBytes:N0} / ↓ {ReceivedBytes:N0} Б · заблокировано {BlockedConnections}"
        : "ПРОКСИ · не проверен";
    public string Detail => Error ?? (ProxyRequired
        ? "Игровой трафик: IPv4/TCP через HTTP CONNECT. UDP/IPv6 и прямые соединения заблокированы."
        : "Прокси отключён: IPv4/TCP через локальный HWID-ретранслятор. UDP/raw и IPv6 заблокированы.");
}
