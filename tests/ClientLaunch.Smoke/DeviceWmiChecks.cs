using System.Runtime.InteropServices;
using System.Text;
using PriceCheck.Collector.Services;

internal static class DeviceWmiChecks
{
    private const uint DigcfPresent = 0x02;
    private const uint DigcfAllClasses = 0x04;
    private const uint CrSuccess = 0;
    private static readonly DevicePropertyKey InstanceIdKey = new(
        Guid.Parse("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);

    public static string FindOriginalDeviceId(string category = "USB/HID")
    {
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (set == new IntPtr(-1)) throw new InvalidOperationException("device enumeration unavailable");
        try
        {
            for (uint index = 0; index < 4096; index++)
            {
                var info = NewInfo();
                if (!SetupDiEnumDeviceInfo(set, index, ref info)) break;
                var id = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, id.Capacity, out _)) continue;
                if (category == "PCI" ? id.ToString().StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) :
                    id.ToString().StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
                    id.ToString().StartsWith("HID\\", StringComparison.OrdinalIgnoreCase))
                    return id.ToString();
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        throw new InvalidOperationException($"no present {category} device for smoke test");
    }

    public static void VerifyChild()
    {
        var original = Environment.GetEnvironmentVariable("PRICECHECK_TEST_DEVICE_ID")!;
        if (CM_Locate_DevNodeW(out var node, original, 0) != CrSuccess)
            throw new InvalidOperationException("original USB/HID devnode could not be located");
        var mapped = new StringBuilder(512);
        if (CM_Get_Device_IDW(node, mapped, (uint)mapped.Capacity, 0) != CrSuccess ||
            mapped.ToString() == original || mapped.Length != original.Length)
            throw new InvalidOperationException("CM_Get_Device_IDW did not map USB/HID identity");
        if (mapped.ToString() != DeviceIdentityPreview.Map(
                Environment.GetEnvironmentVariable("PRICECHECK_HW_UUID")!, original))
            throw new InvalidOperationException("template preview differs from native USB/HID mapping");
        if (CM_Locate_DevNodeW(out var reopened, mapped.ToString(), 0) != CrSuccess || reopened != node)
            throw new InvalidOperationException("mapped USB/HID identity could not be reopened");
        if (CM_Locate_DevNodeW(out var reopenedLower,
                mapped.ToString().ToLowerInvariant(), 0) != CrSuccess || reopenedLower != node)
            throw new InvalidOperationException("mapped USB/HID identity is not case-insensitive");
        var ansiId = new StringBuilder(512);
        if (CM_Get_Device_IDA(node, ansiId, (uint)ansiId.Capacity, 0) != CrSuccess ||
            ansiId.ToString() != mapped.ToString() ||
            CM_Locate_DevNodeA(out var reopenedAnsi, ansiId.ToString(), 0) != CrSuccess || reopenedAnsi != node)
            throw new InvalidOperationException("ANSI Configuration Manager identity differs");
        var pciId = Environment.GetEnvironmentVariable("PRICECHECK_TEST_PCI_ID")!;
        if (CM_Locate_DevNodeW(out var pciNode, pciId, 0) != CrSuccess)
            throw new InvalidOperationException("PCI control devnode could not be located");
        var pciReadback = new StringBuilder(512);
        if (CM_Get_Device_IDW(pciNode, pciReadback, (uint)pciReadback.Capacity, 0) != CrSuccess ||
            pciReadback.ToString() != pciId)
            throw new InvalidOperationException("unrelated PCI identity was modified");

        var property = new byte[1024];
        var key = InstanceIdKey;
        uint bytes = (uint)property.Length;
        if (CM_Get_DevNode_PropertyW(node, ref key, out _, property, ref bytes, 0) != CrSuccess ||
            Encoding.Unicode.GetString(property, 0, checked((int)bytes)).TrimEnd('\0') != mapped.ToString())
            throw new InvalidOperationException("device property identity differs from Configuration Manager");

        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (set == new IntPtr(-1)) throw new InvalidOperationException("SetupAPI enumeration unavailable");
        try
        {
            var info = NewInfo();
            if (!SetupDiOpenDeviceInfoW(set, mapped.ToString(), IntPtr.Zero, 0, ref info))
                throw new InvalidOperationException("mapped USB/HID identity could not be opened through SetupAPI");
            var setupId = new StringBuilder(512);
            if (!SetupDiGetDeviceInstanceIdW(set, ref info, setupId, setupId.Capacity, out _) ||
                setupId.ToString() != mapped.ToString())
                throw new InvalidOperationException("SetupAPI identity differs from Configuration Manager");
            var ansiInfo = NewInfo();
            var setupAnsi = new StringBuilder(512);
            if (!SetupDiOpenDeviceInfoA(set, mapped.ToString(), IntPtr.Zero, 0, ref ansiInfo) ||
                !SetupDiGetDeviceInstanceIdA(set, ref ansiInfo, setupAnsi, setupAnsi.Capacity, out _) ||
                setupAnsi.ToString() != mapped.ToString())
                throw new InvalidOperationException("ANSI SetupAPI identity differs");
            bytes = (uint)property.Length;
            var propertyOk = SetupDiGetDevicePropertyW(set, ref info, ref key, out var propertyType,
                                                        property, bytes, out var required, 0);
            var propertyId = propertyOk
                ? Encoding.Unicode.GetString(property, 0, checked((int)required)).TrimEnd('\0') : "";
            if (!propertyOk || propertyId != mapped.ToString())
                throw new InvalidOperationException($"SetupAPI property identity differs from Configuration Manager " +
                    $"(ok={propertyOk}, error={Marshal.GetLastWin32Error()}, type={propertyType}, " +
                    $"bytes={required}, sameLength={propertyId.Length == mapped.Length}, " +
                    $"original={propertyId == original}, sameIgnoreCase={string.Equals(propertyId, mapped.ToString(), StringComparison.OrdinalIgnoreCase)})");
        }
        finally { SetupDiDestroyDeviceInfoList(set); }

        var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ??
            throw new InvalidOperationException("WMI scripting locator unavailable");
        dynamic locator = Activator.CreateInstance(locatorType)!;
        dynamic services = locator.ConnectServer(".", "root\\cimv2");
        dynamic results = services.ExecQuery("SELECT UUID FROM Win32_ComputerSystemProduct");
        string? uuid = null;
        foreach (dynamic item in results) {
            uuid = item.Properties_.Item("UUID").Value?.ToString();
            break;
        }
        if (!string.Equals(uuid, Environment.GetEnvironmentVariable("PRICECHECK_HW_UUID"),
                           StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WMI system UUID was not mapped");
        dynamic boards = services.ExecQuery("SELECT SerialNumber FROM Win32_BaseBoard");
        var boardSeed = Environment.GetEnvironmentVariable("PRICECHECK_HW_BOARD_SERIAL")!;
        var boardVerified = false;
        foreach (dynamic board in boards)
        {
            string serial = board.Properties_.Item("SerialNumber").Value?.ToString() ?? "";
            if (serial.Length == 0 || serial != new string(Enumerable.Range(0, serial.Length)
                    .Select(index => boardSeed[index % boardSeed.Length]).ToArray()))
                throw new InvalidOperationException("WMI board serial was not mapped");
            boardVerified = true;
            break;
        }
        if (!boardVerified) throw new InvalidOperationException("WMI board row unavailable");
        dynamic devices = services.ExecQuery("SELECT PNPDeviceID FROM Win32_PnPEntity");
        var pnpVerified = false;
        foreach (dynamic device in devices)
        {
            string id = device.Properties_.Item("PNPDeviceID").Value?.ToString() ?? "";
            if (id != mapped.ToString()) continue;
            pnpVerified = true;
            break;
        }
        if (!pnpVerified) throw new InvalidOperationException("WMI PNP device ID differs from SetupAPI");
        dynamic operatingSystems = services.ExecQuery("SELECT Caption FROM Win32_OperatingSystem");
        foreach (dynamic os in operatingSystems)
        {
            if (string.IsNullOrWhiteSpace(os.Properties_.Item("Caption").Value?.ToString()))
                throw new InvalidOperationException("unrelated WMI property was damaged");
            break;
        }
        Console.WriteLine("USB/HID and WMI identity: OK");
    }

    private static DeviceInfoData NewInfo() => new() { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DevicePropertyKey(Guid Format, uint Id);

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
    [DllImport("setupapi.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceIdA(IntPtr set, ref DeviceInfoData info,
        StringBuilder id, int capacity, out int required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiOpenDeviceInfoW(IntPtr set, string id, IntPtr parent,
        uint flags, ref DeviceInfoData info);
    [DllImport("setupapi.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiOpenDeviceInfoA(IntPtr set, string id, IntPtr parent,
        uint flags, ref DeviceInfoData info);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref DeviceInfoData info,
        ref DevicePropertyKey key, out uint type, byte[] data, uint capacity,
        out uint required, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Ansi)]
    private static extern uint CM_Locate_DevNodeA(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, uint capacity, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Ansi)]
    private static extern uint CM_Get_Device_IDA(uint node, StringBuilder id, uint capacity, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_PropertyW(uint node, ref DevicePropertyKey key,
        out uint type, byte[] data, ref uint bytes, uint flags);
}
