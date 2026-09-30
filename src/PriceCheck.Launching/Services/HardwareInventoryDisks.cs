using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace PriceCheck.Collector.Services;

internal static class HardwareInventoryDisks
{
    // Enumerate disk interfaces instead of guessing PhysicalDrive0..15.
    public static IReadOnlyList<int> Indices()
    {
        var guid = new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
        var set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var result = new HashSet<int>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref info))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new System.ComponentModel.Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetailW(set, ref info, IntPtr.Zero, 0, out var needed, IntPtr.Zero);
                if (needed < 8 || needed > 65536) throw new IOException("Invalid disk interface path size.");
                var detail = Marshal.AllocHGlobal(checked((int)needed));
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref info, detail, needed, out _, IntPtr.Zero))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    var path = Marshal.PtrToStringUni(detail + 4)!;
                    using var handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid) continue;
                    var data = new byte[12];
                    if (DeviceIoControl(handle, 0x002D1080, [], 0, data, data.Length, out var returned, IntPtr.Zero) && returned >= 12)
                        result.Add(checked((int)BitConverter.ToUInt32(data, 4)));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result.Order().ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData { public int Size; public Guid Guid; public uint Flags; public UIntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData info, IntPtr detail, uint size, out uint needed, IntPtr device);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
