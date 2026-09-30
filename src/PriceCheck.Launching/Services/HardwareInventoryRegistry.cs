using Microsoft.Win32;

namespace PriceCheck.Collector.Services;

internal static class HardwareInventoryRegistry
{
    internal const string ProfilesPath = @"SYSTEM\CurrentControlSet\Control\IDConfigDB\Hardware Profiles";

    public static void Scan(List<HardwareScanRow> rows)
    {
        var values = new (string Label, string Path, string Name, bool Hooked)[]
        {
            ("Windows · MachineGuid", @"SOFTWARE\Microsoft\Cryptography", "MachineGuid", true),
            ("Windows · ProductId", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductId", true),
            ("Windows · PC name (registry)", @"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName", "ComputerName", true),
            ("Windows · InstallDate", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallDate", true),
            ("Windows · SusClientId", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate", "SusClientId", true),
            ("Windows · SQM MachineId", @"SOFTWARE\Microsoft\SQMClient", "MachineId", true),
            ("Windows · Secure Boot", @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled", false)
        };
        foreach (var value in values) Add(rows, value.Label, value.Path, value.Name, value.Hooked);
        const string processorsPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor";
        try
        {
            using var processors = Registry.LocalMachine.OpenSubKey(processorsPath);
            if (processors is not null)
                foreach (var processor in processors.GetSubKeyNames())
                {
                    var path = processorsPath + @"\" + processor;
                    Add(rows, $"Processor {processor} · model", path, "ProcessorNameString", true);
                    Add(rows, $"Processor {processor} · revision", path, "Update Revision", true);
                }
        }
        catch (Exception error) { rows.Add(new("Processors", error.Message, "Scan unavailable")); }
        try
        {
            using var profiles = Registry.LocalMachine.OpenSubKey(ProfilesPath);
            if (profiles is not null)
                foreach (var profile in profiles.GetSubKeyNames())
                    Add(rows, $"Windows · HwProfileGuid {profile}", ProfilesPath + @"\" + profile, "HwProfileGuid", true);
            Add(rows, "Windows · HwProfileGuid Current", @"SYSTEM\CurrentControlSet\Hardware Profiles\Current", "HwProfileGuid", true);
        }
        catch (Exception error) { rows.Add(new("Windows · HwProfileGuid", error.Message, "Scan unavailable")); }
    }

    private static void Add(List<HardwareScanRow> rows, string label, string path, string name, bool hooked)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            var raw = key?.GetValue(name);
            var current = raw is byte[] bytes ? Convert.ToHexString(bytes) : raw?.ToString();
            if (!string.IsNullOrWhiteSpace(current)) rows.Add(new(label, current,
                hooked ? "RegQueryValueEx · hooked" : "Detected only")
                { RegistryPath = path, RegistryValueName = name });
        }
        catch (Exception error) { rows.Add(new(label, error.Message, "Access denied")); }
    }
}
