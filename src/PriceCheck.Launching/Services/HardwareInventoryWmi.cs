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
            Add(services, rows, "Win32_SystemEnclosure", "SerialNumber", "WMI · Chassis serial");
            Add(services, rows, "Win32_Processor", "SerialNumber", "WMI · Processor serial");
            Add(services, rows, "Win32_Processor", "ProcessorId", "WMI · Processor ID");
            Add(services, rows, "Win32_Processor", "Name", "WMI · Processor name");
            Add(services, rows, "Win32_PhysicalMemory", "SerialNumber", "WMI · Memory serial");
            Add(services, rows, "Win32_DiskDrive", "SerialNumber", "WMI · Disk serial");
            Add(services, rows, "Win32_LogicalDisk", "VolumeSerialNumber", "WMI · Volume serial");
            Add(services, rows, "Win32_ComputerSystem", "Name", "WMI · Computer name");
            AddNetwork(services, rows);
            try { AddMonitors(locator.ConnectServer(".", "root\\wmi"), rows); }
            catch (Exception error) { rows.Add(new("WMI · Monitors", error.Message, "Scan unavailable")); }
        }
        catch (Exception error)
        {
            rows.Add(new("WMI", error.Message, "Scan unavailable"));
        }
    }

    private static void AddNetwork(dynamic services, List<HardwareScanRow> rows)
    {
        try
        {
            foreach (dynamic item in services.ExecQuery("SELECT MACAddress,SettingID FROM Win32_NetworkAdapterConfiguration", "WQL", 0x10))
            {
                string? mac = item.Properties_.Item("MACAddress").Value?.ToString();
                string? id = item.Properties_.Item("SettingID").Value?.ToString();
                if (!string.IsNullOrWhiteSpace(mac) && !string.IsNullOrWhiteSpace(id))
                    rows.Add(new($"WMI · MAC {id}", mac, "IWbemClassObject::Get · hooked") { SourceId = "mac|" + id });
            }
        }
        catch (Exception error) { rows.Add(new("WMI · MAC", error.Message, "Scan unavailable")); }
    }

    private static void AddMonitors(dynamic services, List<HardwareScanRow> rows)
    {
        foreach (dynamic item in services.ExecQuery("SELECT InstanceName,SerialNumberID FROM WmiMonitorID", "WQL", 0x10))
        {
            string instance = item.Properties_.Item("InstanceName").Value?.ToString() ?? "";
            object? value = item.Properties_.Item("SerialNumberID").Value;
            if (value is not Array serial || instance.Length == 0) continue;
            var current = new string(serial.Cast<object>().Select(ch => (char)Convert.ToUInt16(ch)).TakeWhile(ch => ch != '\0').ToArray());
            if (instance.StartsWith(@"DISPLAY\", StringComparison.OrdinalIgnoreCase)) instance = instance[8..];
            instance = System.Text.RegularExpressions.Regex.Replace(instance, @"_\d+$", "");
            rows.Add(new($"WMI · Monitor {instance} · serial", current, "IWbemClassObject::Get · hooked")
                { SourceId = "monitor|" + instance, SourceCapacity = serial.Length });
        }
    }

    private static void Add(dynamic services, List<HardwareScanRow> rows,
                            string category, string property, string label)
    {
        try
        {
            // SWbemObjectSet's Automation enumerator requires a resettable result set.
            // Return immediately keeps provider work asynchronous; ForwardOnly breaks foreach.
            dynamic values = services.ExecQuery($"SELECT {property} FROM {category}", "WQL", 0x10);
            var count = 0;
            foreach (dynamic item in values)
            {
                string current = item.Properties_.Item(property).Value?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(current))
                    rows.Add(new(count == 0 ? label : $"{label} {count + 1}", current,
                        "IWbemClassObject::Get · hooked") { SourceId = category + "." + property });
                count++;
            }
        }
        catch (Exception error) { rows.Add(new(label, error.Message, "Scan unavailable")); }
    }
}
