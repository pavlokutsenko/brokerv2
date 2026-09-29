namespace PriceCheck.Collector.Services;

internal static class HardwareInventoryWmi
{
    public static void Scan(List<HardwareScanRow> rows)
    {
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType is null) throw new InvalidOperationException("WMI locator unavailable");
            dynamic locator = Activator.CreateInstance(locatorType)!;
            dynamic services = locator.ConnectServer(".", "root\\cimv2");
            Add(services, rows, "Win32_ComputerSystemProduct", "UUID", "WMI · System UUID");
            Add(services, rows, "Win32_ComputerSystemProduct", "IdentifyingNumber", "WMI · System serial");
            Add(services, rows, "Win32_BaseBoard", "SerialNumber", "WMI · Baseboard serial");
            Add(services, rows, "Win32_Processor", "ProcessorId", "WMI · Processor ID");
            Add(services, rows, "Win32_Processor", "Name", "WMI · Processor name");
            Add(services, rows, "Win32_PhysicalMemory", "SerialNumber", "WMI · Memory serial");
            Add(services, rows, "Win32_DiskDrive", "SerialNumber", "WMI · Disk serial");
            Add(services, rows, "Win32_LogicalDisk", "VolumeSerialNumber", "WMI · Volume serial");
            Add(services, rows, "Win32_ComputerSystem", "Name", "WMI · Computer name");
        }
        catch (Exception error)
        {
            rows.Add(new("WMI", error.Message, "Scan unavailable"));
        }
    }

    private static void Add(dynamic services, List<HardwareScanRow> rows,
                            string category, string property, string label)
    {
        try
        {
            dynamic values = services.ExecQuery($"SELECT {property} FROM {category}");
            var count = 0;
            foreach (dynamic item in values)
            {
                string current = item.Properties_.Item(property).Value?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(current))
                    rows.Add(new(count == 0 ? label : $"{label} {count + 1}", current,
                        "IWbemClassObject::Get · hooked"));
                if (++count >= 16) break;
            }
        }
        catch (Exception error) { rows.Add(new(label, error.Message, "Scan unavailable")); }
    }
}
