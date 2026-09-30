using Microsoft.Win32;
using System.Net.NetworkInformation;

namespace PriceCheck.Collector.Services;

internal static class HardwareInventoryNetwork
{
    internal const string ClassPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    public static void Scan(List<HardwareScanRow> rows)
    {
        try
        {
            var paths = RegistryPaths();
            var selectedGateway = GatewayIdentityService.FindIpv4Gateway();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                var bytes = adapter.GetPhysicalAddress().GetAddressBytes();
                rows.Add(new($"MAC · {adapter.Name}", bytes.Length == 6 ? Convert.ToHexString(bytes) : "MAC unavailable",
                    bytes.Length == 6 ? "GetAdaptersAddresses · hooked" : "Detected only")
                    { SourceId = adapter.Id, RegistryPath = paths.GetValueOrDefault(Normalize(adapter.Id), ""),
                      RegistryValueName = "NetworkAddress" });
                foreach (var gateway in adapter.GetIPProperties().GatewayAddresses)
                {
                    if (gateway.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    var routerMac = HardwareInventoryNative.TryRouterMac(gateway.Address);
                    rows.Add(new($"Gateway {gateway.Address} · MAC", routerMac ?? "ARP unresolved",
                        routerMac is null ? "ARP unavailable" : gateway.Address.Equals(selectedGateway)
                            ? "SendARP · gateway hook" : "Detected only"));
                }
            }
        }
        catch (Exception error) { rows.Add(new("Network adapters", error.Message, "Scan unavailable")); }
    }

    private static Dictionary<string, string> RegistryPaths()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(ClassPath);
            if (root is null) return result;
            foreach (var instance in root.GetSubKeyNames())
            {
                if (instance.Length != 4 || !instance.All(char.IsAsciiDigit)) continue;
                using var key = root.OpenSubKey(instance);
                if (key?.GetValue("NetCfgInstanceId") is string id)
                    result[Normalize(id)] = ClassPath + @"\" + instance;
            }
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }

    private static string Normalize(string id) => id.Trim('{', '}');
}
