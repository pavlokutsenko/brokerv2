using System.Text;

namespace PriceCheck.Collector.Services;

public sealed record HardwareScanRow(string Label, string CurrentValue, string Coverage, string TargetValue = "")
{
    public string DriverCoverage => Coverage is "Detected only" or "Access denied" or "Scan unavailable" or "Invalid EDID" or "Read failed"
        ? "Подмена не подтверждена / источник недоступен"
        : RegistryValueName is "MachineGuid" or "HwProfileGuid" or "EDID" or "ProductId" or "SusClientId" or "MachineId"
            or "VideoIdentifier" or "ComputerName" or "ProcessorNameString" or "Update Revision" or "InstallDate"
            ? "Драйвер · PID/шаблон + общий набор первого клиента (нужна новая сборка)"
            : "Агент · только клиент и его шаблон";
    public string SourceId { get; init; } = "";
    public int SourceCapacity { get; init; }
    public string RegistryPath { get; init; } = "";
    public string RegistryValueName { get; init; } = "";
}

public static class HardwareInventoryService
{
    public static IReadOnlyList<HardwareScanRow> Scan()
    {
        var rows = new List<HardwareScanRow>();
        try { ScanFirmware(rows); } catch (Exception e) { rows.Add(new("SMBIOS", e.Message, "Scan unavailable")); }
        HardwareInventoryRegistry.Scan(rows);
        HardwareInventoryNetwork.Scan(rows);
        ScanVolumes(rows);
        try { ScanDisks(rows); } catch (Exception e) { rows.Add(new("Physical disks", e.Message, "Scan unavailable")); }
        try { HardwareInventoryMonitors.Scan(rows); } catch (Exception e) { rows.Add(new("Monitors", e.Message, "Scan unavailable")); }
        HardwareInventoryPeripheral.Scan(rows);
        HardwareInventoryWmi.Scan(rows);
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
        foreach (var index in HardwareInventoryDisks.Indices())
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

}
