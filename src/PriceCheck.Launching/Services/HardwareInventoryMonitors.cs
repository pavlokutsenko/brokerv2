using Microsoft.Win32;

namespace PriceCheck.Collector.Services;

internal static class HardwareInventoryMonitors
{
    public static void Scan(List<HardwareScanRow> rows)
    {
        using var display = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
        if (display is null) return;
        foreach (var vendor in display.GetSubKeyNames())
        using (var vendorKey = display.OpenSubKey(vendor))
        {
            if (vendorKey is null) continue;
            foreach (var instance in vendorKey.GetSubKeyNames())
            {
                var id = vendor + @"\" + instance;
                var path = @"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + id + @"\Device Parameters";
                try
                {
                    using var parameters = Registry.LocalMachine.OpenSubKey(path);
                    if (parameters?.GetValue("EDID") is not byte[] edid)
                    {
                        rows.Add(new($"Monitor {id} · EDID", "EDID unavailable", "Detected only") { SourceId = id });
                        continue;
                    }
                    using var overrides = parameters.OpenSubKey("EDID_OVERRIDE");
                    if (overrides is not null)
                        for (var block = 0; block * 128 + 128 <= edid.Length; block++)
                            if (overrides.GetValue(block.ToString(System.Globalization.CultureInfo.InvariantCulture)) is byte[] value && value.Length == 128)
                                value.CopyTo(edid, block * 128);
                    rows.Add(new($"Monitor {id} · EDID", Convert.ToHexString(edid),
                        MonitorIdentityPreview.IsValid(edid) ? "RegQueryValueEx · EDID hook" : "Invalid EDID")
                        { SourceId = id, RegistryPath = path, RegistryValueName = "EDID" });
                }
                catch (Exception error) { rows.Add(new($"Monitor {id} · EDID", error.Message, "Access denied") { SourceId = id }); }
            }
        }
    }
}
