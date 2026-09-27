namespace PriceCheck.Contracts;

public sealed record ClientProtectionStatus(bool HardwareReady, bool WorldIdentityApplied,
    bool ProxyReady, long ProxiedConnections, long SentBytes, long ReceivedBytes,
    string IdentityTag, string? Error, ulong BlockedConnections = 0, bool ProxyRequired = true)
{
    public static ClientProtectionStatus Pending { get; } = new(false, false, false, 0, 0, 0, "", null);
    public bool Failed => Error is not null;
    public string HardwareLabel => Failed ? (Error!.StartsWith("HWID:", StringComparison.Ordinal)
        ? "HWID · ОШИБКА" : "HWID · проверка прервана") : WorldIdentityApplied
        ? $"HWID · применён в мире · {IdentityTag}" : HardwareReady
        ? $"HWID · проверен, ожидание входа · {IdentityTag}" : "HWID · не проверен";
    public string ProxyLabel => !ProxyRequired ? "ПРОКСИ · выключен в шаблоне" : Failed ? "ПРОКСИ · ОШИБКА" : ProxyReady
        ? $"ПРОКСИ · защита активна · {ProxiedConnections} CONNECT · ↑ {SentBytes:N0} / ↓ {ReceivedBytes:N0} Б · блокировано {BlockedConnections}"
        : "ПРОКСИ · не проверен";
    public string Detail => Error ?? (ProxyRequired
        ? "Трафик игровых процессов: IPv4/TCP через HTTP CONNECT. UDP/IPv6 и прямые подключения блокируются."
        : "Прокси выключен: IPv4/TCP идёт напрямую к серверам через локальный relay проверки HWID. UDP/raw и IPv6 блокируются.");
}
