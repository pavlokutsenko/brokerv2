using Microsoft.Win32;

namespace PriceCheck.Collector.Services;

internal static class HardwareInventoryPeripheral
{
    private const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string MediaClass = "{4d36e96c-e325-11ce-bfc1-08002be10318}";

    public static void Scan(List<HardwareScanRow> rows)
    {
        ScanPci(rows);
        ScanVideoIdentifiers(rows);
        ScanUsb(rows, "USB", 24);
        ScanUsb(rows, "HID", 24);
    }

    private static void ScanVideoIdentifiers(List<HardwareScanRow> rows)
    {
        try
        {
            using var video = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (video is null) return;
            foreach (var device in video.GetSubKeyNames())
            using (var deviceKey = video.OpenSubKey(device))
            {
                if (deviceKey is null) continue;
                var value = deviceKey.GetValue("VideoIdentifier")?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    rows.Add(new("Windows · VideoIdentifier", value, "RegQueryValueEx · hooked"));
            }
        }
        catch (Exception e) { rows.Add(new("Video · registry", e.Message, "Access denied")); }
    }

    private static void ScanPci(List<HardwareScanRow> rows)
    {
        try
        {
            using var pci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");
            if (pci is null) return;
            foreach (var device in pci.GetSubKeyNames())
            using (var deviceKey = pci.OpenSubKey(device))
            {
                if (deviceKey is null) continue;
                foreach (var instance in deviceKey.GetSubKeyNames())
                using (var key = deviceKey.OpenSubKey(instance))
                {
                    var deviceClass = key?.GetValue("ClassGUID")?.ToString();
                    var category = deviceClass?.ToLowerInvariant() switch
                    {
                        DisplayClass => "Display adapter",
                        MediaClass => "Audio device",
                        _ => null
                    };
                    if (category is null) continue;
                    var ids = key?.GetValue("HardwareID") as string[];
                    var name = key?.GetValue("FriendlyName")?.ToString() ?? key?.GetValue("DeviceDesc")?.ToString() ?? device;
                    var id = ids?.FirstOrDefault() ?? device;
                    rows.Add(new($"{category} · {name}", id, "Detected only"));
                }
            }
        }
        catch (Exception e) { rows.Add(new("PCI devices", e.Message, "Access denied")); }
    }

    private static void ScanUsb(List<HardwareScanRow> rows, string branch, int limit)
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + branch);
            if (root is null) return;
            var count = 0;
            foreach (var device in root.GetSubKeyNames())
            using (var deviceKey = root.OpenSubKey(device))
            {
                if (deviceKey is null) continue;
                foreach (var instance in deviceKey.GetSubKeyNames())
                {
                    if (count++ >= limit) break;
                    rows.Add(new($"{branch} · {device}", instance, "Detected only"));
                }
                if (count >= limit) break;
            }
            if (count >= limit) rows.Add(new($"{branch} · more", "Showing the first 24 devices", "Detected only"));
        }
        catch (Exception e) { rows.Add(new($"{branch} devices", e.Message, "Access denied")); }
    }
}
