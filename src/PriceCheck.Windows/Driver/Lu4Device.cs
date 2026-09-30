using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PriceCheck.Collector.Runtime.Driver;

public sealed partial class Lu4Device : IDisposable
{
    private const uint Version = 3;
    private const uint DeviceType = 0x8337;
    private const uint ReadAccess = 1;
    private const uint WriteAccess = 2;
    private const int CopyHeaderSize = 24;
    private const int VirtualMemorySize = 40;
    private readonly SafeFileHandle _handle;

    public Lu4Device()
    {
        _handle = CreateFile(@"\\.\LU4Memory", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "LU4Memory driver is not running.");
    }

    public ulong GetProcessBase(int pid)
    {
        var request = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(request, Version);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(4), pid);
        var response = Ioctl(Code(0x800, ReadAccess), request, 16);
        Validate(response, 16);
        if (BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(4)) != pid)
            throw new IOException("LU4Memory base response belongs to another PID.");
        return BinaryPrimitives.ReadUInt64LittleEndian(response.AsSpan(8));
    }

    public ClientProcessStatus QueryProcessStatus(int pid)
    {
        var request = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(request, Version);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(4), pid);
        var response = Ioctl(Code(0x811, ReadAccess), request, request.Length);
        Validate(response, request.Length);
        if (BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(4)) != pid)
            throw new IOException("LU4Memory process status belongs to another PID.");
        var created = BinaryPrimitives.ReadInt64LittleEndian(response.AsSpan(8));
        var active = BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(16)) == 1;
        if (active && created <= 0) throw new IOException("LU4Memory process creation time is missing.");
        return new(pid, created > 0 ? new DateTimeOffset(DateTime.FromFileTimeUtc(created)) : null,
            active, BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(20)));
    }

    public byte[] Read(int pid, ulong address, int size)
    {
        if (size <= 0 || size > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(size));
        var request = CopyHeader(pid, address, size);
        var response = Ioctl(Code(0x801, ReadAccess), request, CopyHeaderSize + size);
        if (response.Length < CopyHeaderSize) throw new IOException("LU4Memory returned a short read response.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(response) != Version ||
            BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(4)) != pid ||
            BinaryPrimitives.ReadUInt64LittleEndian(response.AsSpan(8)) != address ||
            BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(16)) != size)
            throw new IOException("LU4Memory read response does not match this PID/address request.");
        var copied = BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(20));
        if (copied != size) throw new IOException($"LU4Memory read {copied} of {size} bytes.");
        return response.AsSpan(CopyHeaderSize, copied).ToArray();
    }

    public void Write(int pid, ulong address, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || data.Length > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(data));
        var request = new byte[CopyHeaderSize + data.Length];
        CopyHeader(pid, address, data.Length).CopyTo(request, 0);
        data.CopyTo(request.AsSpan(CopyHeaderSize));
        var response = Ioctl(Code(0x802, WriteAccess), request, CopyHeaderSize);
        if (response.Length < CopyHeaderSize || BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(20)) != data.Length)
            throw new IOException("LU4Memory did not write all bytes.");
    }

    public ulong Allocate(int pid, ulong size)
    {
        var request = VirtualMemoryRequest(pid, 0, size, 0x40);
        var response = Ioctl(Code(0x80A, WriteAccess), request, VirtualMemorySize);
        Validate(response, VirtualMemorySize);
        return BinaryPrimitives.ReadUInt64LittleEndian(response.AsSpan(8));
    }

    public uint Protect(int pid, ulong address, ulong size, uint protection)
    {
        var response = Ioctl(Code(0x80B, WriteAccess), VirtualMemoryRequest(pid, address, size, protection), VirtualMemorySize);
        Validate(response, VirtualMemorySize);
        return BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(28));
    }

    public void Free(int pid, ulong address)
    {
        var response = Ioctl(Code(0x80C, WriteAccess), VirtualMemoryRequest(pid, address, 0, 0), VirtualMemorySize);
        Validate(response, VirtualMemorySize);
    }

    public void SetProxyRedirect(int pid, int listenerPort, bool enabled)
    {
        if (pid <= 0 || listenerPort is < 0 or > 65535 || enabled && listenerPort == 0)
            throw new ArgumentOutOfRangeException(nameof(listenerPort));
        var request = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(request, Version);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(4), pid);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(12), listenerPort);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(16), enabled ? 1 : 0);
        var response = Ioctl(Code(0x80D, WriteAccess), request, request.Length);
        Validate(response, request.Length);
        var status = BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(20));
        if (status < 0)
            throw new IOException($"WFP registration failed: NTSTATUS 0x{status:X8}, stage {BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(24))}.");
    }

    public void Dispose() => _handle.Dispose();

    public ProxyGuardStatus QueryProxyGuard(int pid = 0)
    {
        var request = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(request, Version);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(4), pid);
        var response = Ioctl(Code(0x810, ReadAccess), request, 48);
        if (response.Length is not (32 or 48)) throw new IOException("Invalid LU4Memory guard response.");
        Validate(response, response.Length);
        return new(pid, BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(8)),
            BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(12)),
            BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(16)),
            BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(20)) == 1,
            BinaryPrimitives.ReadUInt64LittleEndian(response.AsSpan(24)),
            response.Length >= 48 ? BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(32)) : 0,
            response.Length >= 48 ? BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(36)) : 0,
            response.Length >= 48 ? BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(40)) : 0,
            response.Length >= 48 ? BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(44)) : 0);
    }

    private byte[] Ioctl(uint code, byte[] input, int outputSize)
    {
        var output = new byte[outputSize];
        if (!DeviceIoControl(_handle, code, input, input.Length, output, output.Length, out var returned, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"LU4Memory IOCTL 0x{code:X8}");
        return output.AsSpan(0, returned).ToArray();
    }

    private static byte[] CopyHeader(int pid, ulong address, int size)
    {
        var value = new byte[CopyHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(value, Version);
        BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(4), pid);
        BinaryPrimitives.WriteUInt64LittleEndian(value.AsSpan(8), address);
        BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(16), size);
        return value;
    }

    private static byte[] VirtualMemoryRequest(int pid, ulong address, ulong size, uint protection)
    {
        var value = new byte[VirtualMemorySize];
        BinaryPrimitives.WriteUInt32LittleEndian(value, Version);
        BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(4), pid);
        BinaryPrimitives.WriteUInt64LittleEndian(value.AsSpan(8), address);
        BinaryPrimitives.WriteUInt64LittleEndian(value.AsSpan(16), size);
        BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(24), protection);
        return value;
    }

    private static uint Code(uint function, uint access) =>
        (DeviceType << 16) | (access << 14) | (function << 2);

    private static void Validate(byte[] response, int expected)
    {
        if (response.Length != expected || BinaryPrimitives.ReadUInt32LittleEndian(response) != Version)
            throw new IOException("Invalid LU4Memory response.");
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize,
        byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
