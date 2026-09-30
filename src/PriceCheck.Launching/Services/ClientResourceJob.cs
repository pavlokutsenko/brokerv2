using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PriceCheck.Collector.Services;

// Assign the suspended launcher so its game child inherits per-process limits.
internal sealed class ClientResourceJob : IDisposable
{
    private readonly SafeJobHandle _handle;
    private readonly int? _pendingCpuPercent;

    private ClientResourceJob(SafeJobHandle handle, int? cpuPercent)
    {
        _handle = handle;
        _pendingCpuPercent = cpuPercent;
    }

    public static ClientResourceJob Assign(SuspendedClientProcess launcher, int? maximumMiB, int? cpuPercent)
    {
        if (maximumMiB is null && cpuPercent is null) throw new ArgumentException("At least one resource limit is required.");
        if (maximumMiB is < 256 or > 65536) throw new ArgumentOutOfRangeException(nameof(maximumMiB));
        if (cpuPercent is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(cpuPercent));
        var handle = CreateJobObjectW(nint.Zero, null);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a client resource job.");
        try
        {
            if (maximumMiB is { } memoryMiB)
            {
                var limits = new BasicLimitInformation
                {
                    LimitFlags = 1, // JOB_OBJECT_LIMIT_WORKINGSET
                    MinimumWorkingSetSize = (nuint)(20 * Environment.SystemPageSize),
                    MaximumWorkingSetSize = checked((nuint)((ulong)memoryMiB * 1024 * 1024))
                };
                if (!SetInformationJobObject(handle, 2, ref limits, (uint)Marshal.SizeOf<BasicLimitInformation>()))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure the memory-budget job.");
            }
            if (!AssignProcessToJobObject(handle, launcher.NativeHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not assign the suspended launcher to its resource job.");
            return new ClientResourceJob(handle, cpuPercent);
        }
        catch { handle.Dispose(); throw; }
    }

    public bool Contains(int pid)
    {
        using var process = OpenProcess(0x1000, false, (uint)pid);
        if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect the game process job.");
        if (!IsProcessInJob(process, _handle, out var member))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not verify the game process job.");
        return member;
    }

    public int? ReadCpuPercent()
    {
        if (!QueryInformationJobObject(_handle, 15, out var cpu,
                (uint)Marshal.SizeOf<CpuRateControlInformation>(), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read back the CPU budget.");
        return (cpu.ControlFlags & 0x5) == 0x5 ? checked((int)cpu.CpuRate / 100) : null;
    }

    public int? ApplyCpuBudget()
    {
        if (_pendingCpuPercent is not { } percent) return null;
        var cpu = new CpuRateControlInformation { ControlFlags = 0x1 | 0x4,
            CpuRate = checked((uint)percent * 100) };
        if (!SetInformationJobObject(_handle, 15, ref cpu, (uint)Marshal.SizeOf<CpuRateControlInformation>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure the CPU budget.");
        if (ReadCpuPercent() != percent)
            throw new IOException("Windows did not confirm the CPU budget.");
        return percent;
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CpuRateControlInformation
    {
        public uint ControlFlags;
        public uint CpuRate;
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeJobHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeJobHandle job, int informationClass,
        ref BasicLimitInformation information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeJobHandle job, int informationClass,
        ref CpuRateControlInformation information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeJobHandle job, int informationClass,
        out CpuRateControlInformation information, uint length, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, nint process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(SafeProcessHandle process, SafeJobHandle job,
        [MarshalAs(UnmanagedType.Bool)] out bool result);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
