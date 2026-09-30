using System.Runtime.InteropServices;
using System.Text;
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
        ScanDeviceIds(rows);
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
                    rows.Add(new("Windows · VideoIdentifier", value, "RegQueryValueEx · hooked")
                        { RegistryPath = @"SYSTEM\CurrentControlSet\Control\Video\" + device, RegistryValueName = "VideoIdentifier" });
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
                    foreach (var id in ids is { Length: > 0 } ? ids : [device])
                        rows.Add(new($"{category} · {name}", id, "Detected only") { SourceId = device + @"\" + instance });
                }
            }
        }
        catch (Exception e) { rows.Add(new("PCI devices", e.Message, "Access denied")); }
    }

    private static void ScanDeviceIds(List<HardwareScanRow> rows)
    {
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, 0x06);
        if (set == new IntPtr(-1))
        {
            rows.Add(new("USB/HID API", "Device enumeration unavailable", "Scan unavailable"));
            return;
        }
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new System.ComponentModel.Win32Exception(error);
                }
                var id = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, id.Capacity, out _)) continue;
                var value = id.ToString();
                var isUsb = value.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase);
                var isHid = value.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase);
                if (!isUsb && !isHid) continue;
                rows.Add(new($"{(isUsb ? "USB" : "HID")} API · {value.Split('\\')[1]}",
                    value, "SetupAPI/CM · hooked") { SourceId = value });
            }
        }
        catch (Exception e) { rows.Add(new("USB/HID API", e.Message, "Scan unavailable")); }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator,
        IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfoData info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref DeviceInfoData info,
        StringBuilder id, int capacity, out int required);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
