using System.Runtime.InteropServices;
using System.Text;
using System.Net;
using Microsoft.Win32.SafeHandles;

namespace PriceCheck.Collector.Services;

internal static class HardwareInventoryNative
{
    private const uint FirmwareProvider = 0x52534D42; // 'RSMB'
    private const uint StorageQuery = 0x002D1400;
    private const uint DriveLayoutQuery = 0x00070050;

    public static byte[] ReadSmbios()
    {
        var size = GetSystemFirmwareTable(FirmwareProvider, 0, null, 0);
        if (size is < 8 or > 1024 * 1024) return [];
        var result = new byte[size];
        return GetSystemFirmwareTable(FirmwareProvider, 0, result, size) == size ? result : [];
    }

    public static bool TryVolumeSerial(string root, out uint serial) =>
        GetVolumeInformationW(root, null, 0, out serial, IntPtr.Zero, IntPtr.Zero, null, 0);

    public static string? TryDiskSerial(int index)
    {
        using var handle = CreateFileW($@"\\.\PhysicalDrive{index}", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var query = new byte[12]; // STORAGE_PROPERTY_QUERY includes AdditionalParameters[1]
        var output = new byte[1024];
        if (!DeviceIoControl(handle, StorageQuery, query, query.Length, output, output.Length,
                out var returned, IntPtr.Zero) || returned < 28) return null;
        var offset = BitConverter.ToUInt32(output, 24);
        if (offset == 0 || offset >= returned) return null;
        var end = (int)offset;
        while (end < returned && output[end] != 0) end++;
        return Encoding.ASCII.GetString(output, (int)offset, end - (int)offset);
    }

    public static string? TryRouterMac(IPAddress address)
    {
        var bytes = new byte[8];
        var length = bytes.Length;
        var destination = BitConverter.ToUInt32(address.GetAddressBytes());
        return SendARP(destination, 0, bytes, ref length) == 0 && length >= 6
            ? Convert.ToHexString(bytes, 0, 6) : null;
    }

    public static (string Kind, string Value)? TryDiskLayout(int index)
    {
        using var handle = CreateFileW($@"\\.\PhysicalDrive{index}", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var output = new byte[4096];
        if (!DeviceIoControl(handle, DriveLayoutQuery, Array.Empty<byte>(), 0, output, output.Length,
                out var returned, IntPtr.Zero) || returned < 24) return null;
        var style = BitConverter.ToUInt32(output, 0);
        return style switch
        {
            0 => ("MBR ID", BitConverter.ToUInt32(output, 8).ToString("X8")),
            1 => ("GPT GUID", new Guid(output.AsSpan(8, 16)).ToString("D")),
            _ => null
        };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(uint provider, uint table, byte[]? buffer, uint size);
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SendARP(uint destination, uint source, byte[] mac, ref int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(string root, StringBuilder? name, uint nameSize,
        out uint serial, IntPtr maxComponent, IntPtr flags, StringBuilder? filesystem, uint filesystemSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize,
        byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
