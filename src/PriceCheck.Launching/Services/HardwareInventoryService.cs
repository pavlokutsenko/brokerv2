using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Win32;

namespace PriceCheck.Collector.Services;

public sealed record HardwareScanRow(string Label, string CurrentValue, string Coverage, string TargetValue = "");

public static class HardwareInventoryService
{
    public static IReadOnlyList<HardwareScanRow> Scan()
    {
        var rows = new List<HardwareScanRow>();
        try { ScanFirmware(rows); } catch (Exception e) { rows.Add(new("SMBIOS", e.Message, "Scan unavailable")); }
        ScanRegistry(rows);
        ScanNetwork(rows);
        ScanVolumes(rows);
        try { ScanDisks(rows); } catch (Exception e) { rows.Add(new("Physical disks", e.Message, "Scan unavailable")); }
        try { ScanMonitors(rows); } catch (Exception e) { rows.Add(new("Monitors", e.Message, "Scan unavailable")); }
        HardwareInventoryPeripheral.Scan(rows);
        return rows;
    }

    private static void ScanFirmware(List<HardwareScanRow> rows)
    {
        var table = HardwareInventoryNative.ReadSmbios();
        if (table.Length < 8) return;
        var position = 8;
        var memoryIndex = 0;
        while (position + 4 <= table.Length)
        {
            var type = table[position];
            var length = table[position + 1];
            if (length < 4 || position + length > table.Length) break;
            var strings = position + length;
            var end = strings;
            while (end + 1 < table.Length && !(table[end] == 0 && table[end + 1] == 0)) end++;
            if (end + 1 >= table.Length) break;
            string Value(int offset) => position + offset < position + length
                ? ReadSmbiosString(table, strings, end, table[position + offset]) : "";
            void Add(string label, string value, bool hooked)
            {
                if (!string.IsNullOrWhiteSpace(value)) rows.Add(new(label, value,
                    hooked ? "GetSystemFirmwareTable · hooked" : "Detected only"));
            }
            switch (type)
            {
                case 0:
                    Add("BIOS · vendor", Value(4), false);
                    Add("BIOS · version", Value(5), false);
                    break;
                case 1:
                    Add("System · vendor", Value(4), false);
                    Add("System · model", Value(5), false);
                    Add("System · serial", Value(7), true);
                    if (length >= 24) Add("System · UUID", new Guid(table.AsSpan(position + 8, 16)).ToString("D"), true);
                    break;
                case 2:
                    Add("Baseboard · vendor", Value(4), false);
                    Add("Baseboard · model", Value(5), false);
                    Add("Baseboard · serial", Value(7), true);
                    break;
                case 3:
                    Add("Chassis · serial", Value(7), true);
                    break;
                case 4:
                    if (length >= 16) Add("Processor · ID", Convert.ToHexString(table, position + 8, 8), true);
                    if (length > 0x20) Add("Processor · serial", Value(0x20), true);
                    break;
                case 17:
                    if (length > 0x18) Add($"Memory {++memoryIndex} · serial", Value(0x18), true);
                    break;
            }
            position = end + 2;
            if (type == 127) break;
        }
    }

    private static string ReadSmbiosString(byte[] table, int first, int end, byte index)
    {
        if (index == 0) return "";
        var position = first;
        for (var current = 1; current < index; current++)
        {
            while (position < end && table[position] != 0) position++;
            position++;
        }
        if (position >= end) return "";
        var last = position;
        while (last < end && table[last] != 0) last++;
        return Encoding.Latin1.GetString(table, position, last - position);
    }

    private static void ScanRegistry(List<HardwareScanRow> rows)
    {
        var values = new (string Label, string Path, string Name, bool Hooked)[]
        {
            ("Windows · MachineGuid", @"SOFTWARE\Microsoft\Cryptography", "MachineGuid", true),
            ("Windows · HwProfileGuid", @"SYSTEM\CurrentControlSet\Control\IDConfigDB\Hardware Profiles\0001", "HwProfileGuid", true),
            ("Windows · ProductId", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductId", true),
            ("Windows · PC name (registry)", @"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName", "ComputerName", true),
            ("Windows · InstallDate", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallDate", true),
            ("Windows · SusClientId", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate", "SusClientId", true),
            ("Windows · SQM MachineId", @"SOFTWARE\Microsoft\SQMClient", "MachineId", false),
            ("Windows · Secure Boot", @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled", false),
            ("Processor · model", @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", false),
            ("Processor · revision", @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "Update Revision", false)
        };
        foreach (var value in values)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(value.Path);
                var raw = key?.GetValue(value.Name);
                var current = raw is byte[] bytes ? Convert.ToHexString(bytes) : raw?.ToString();
                if (!string.IsNullOrWhiteSpace(current)) rows.Add(new(value.Label, current,
                    value.Hooked ? "RegQueryValueEx · hooked" : "Detected only"));
            }
            catch (Exception e) { rows.Add(new(value.Label, e.Message, "Access denied")); }
        }
    }

    private static void ScanNetwork(List<HardwareScanRow> rows)
    {
        var selectedGateway = GatewayIdentityService.FindIpv4Gateway();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            var bytes = adapter.GetPhysicalAddress().GetAddressBytes();
            if (bytes.Length != 6) continue;
            rows.Add(new($"MAC · {adapter.Name}", Convert.ToHexString(bytes), "GetAdaptersAddresses · hooked"));
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

    private static void ScanVolumes(List<HardwareScanRow> rows)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || !HardwareInventoryNative.TryVolumeSerial(drive.RootDirectory.FullName, out var serial)) continue;
                rows.Add(new($"Volume {drive.Name} · ID", serial.ToString("X8"), "GetVolumeInformation · hooked"));
            }
            catch (Exception e) { rows.Add(new($"Volume {drive.Name}", e.Message, "Access denied")); }
        }
    }

    private static void ScanDisks(List<HardwareScanRow> rows)
    {
        var found = false;
        for (var index = 0; index < 16; index++)
        {
            var serial = HardwareInventoryNative.TryDiskSerial(index);
            var layout = HardwareInventoryNative.TryDiskLayout(index);
            if (!string.IsNullOrWhiteSpace(serial))
            {
                found = true;
                rows.Add(new($"Disk {index} · serial", serial, "DeviceIoControl · hooked"));
            }
            if (layout is { } value)
            {
                found = true;
                rows.Add(new($"Disk {index} · {value.Kind}", value.Value, "DeviceIoControl · hooked"));
            }
        }
        if (!found) rows.Add(new("Disks · serial", "Storage descriptor unavailable", "Read failed"));
    }

    private static void ScanMonitors(List<HardwareScanRow> rows)
    {
        using var display = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
        if (display is null) return;
        foreach (var vendor in display.GetSubKeyNames())
        using (var vendorKey = display.OpenSubKey(vendor))
        {
            if (vendorKey is null) continue;
            foreach (var instance in vendorKey.GetSubKeyNames())
            using (var parameters = vendorKey.OpenSubKey(instance + @"\Device Parameters"))
            {
                if (parameters?.GetValue("EDID") is not byte[] edid || edid.Length < 16) continue;
                rows.Add(new($"Monitor {vendor} · EDID", Convert.ToHexString(edid, 12, 4), "Detected only"));
            }
        }
    }
}
