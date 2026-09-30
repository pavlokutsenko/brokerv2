using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PriceCheck.Collector.Runtime.Driver;

public sealed partial class Lu4Device
{
    public WorkingSetBounds ApplyWorkingSetBudget(PriceCheck.Contracts.ClientSession session, int maximumMiB)
    {
        if (maximumMiB is < 256 or > 65536) throw new ArgumentOutOfRangeException(nameof(maximumMiB));
        var original = ReadWorkingSetBounds(session);
        var maximum = checked((ulong)maximumMiB * 1024 * 1024);
        try
        {
            SetWorkingSetBounds(session, Math.Min(original.MinimumBytes, maximum), maximum, 6, 1);
            var actual = ReadWorkingSetBounds(session);
            if (actual.MaximumBytes != maximum || (actual.Flags & 12) != 4 || (actual.Flags & 3) != 2)
                throw new IOException("Windows did not confirm the resident-memory budget.");
            return original;
        }
        catch (Exception applyError)
        {
            var unchanged = false;
            try { unchanged = ReadWorkingSetBounds(session) == original; }
            catch (Exception) { /* An unreadable process is not evidence of rollback. */ }
            if (unchanged) throw;
            // An error after ZwSetInformationProcess can still leave the policy changed.
            // Never restore through a recycled PID; both operations check its birth time.
            try { RestoreWorkingSetBudget(session, original); }
            catch (Exception restoreError)
            {
                throw new AggregateException("Budget failed; original bounds could not be confirmed. Inspect the client before retrying.",
                    applyError, restoreError);
            }
            throw;
        }
    }

    public void RestoreWorkingSetBudget(PriceCheck.Contracts.ClientSession session, WorkingSetBounds original)
    {
        SetWorkingSetBounds(session, original.MinimumBytes, original.MaximumBytes, original.Flags, 2);
        if (ReadWorkingSetBounds(session) != original)
            throw new IOException("Windows did not confirm the original resident-memory bounds.");
    }

    public static WorkingSetBounds ReadWorkingSetBounds(PriceCheck.Contracts.ClientSession session)
    {
        if (!PriceCheck.Windows.ClientProcessIdentity.IsCurrent(session)) throw new IOException("Client PID changed.");
        using var handle = OpenWorkingSetProcess(0x1000, false, session.ProcessId);
        if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (!GetWorkingSetProcessTimes(handle, out var birth, out _, out _, out _))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (birth != session.StartedAtUtc.UtcDateTime.ToFileTimeUtc()) throw new IOException("Client PID changed during quota query.");
        if (!GetProcessWorkingSetSizeEx(handle, out var minimum, out var maximum, out var flags))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (!PriceCheck.Windows.ClientProcessIdentity.IsCurrent(session)) throw new IOException("Client PID changed during quota query.");
        return new(minimum.ToUInt64(), maximum.ToUInt64(), flags);
    }

    private void SetWorkingSetBounds(PriceCheck.Contracts.ClientSession session, ulong minimum, ulong maximum, uint flags, uint operation)
    {
        if (!PriceCheck.Windows.ClientProcessIdentity.IsCurrent(session)) throw new IOException("Client PID changed.");
        var request = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(request, Version);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(4), session.ProcessId);
        BinaryPrimitives.WriteUInt64LittleEndian(request.AsSpan(8), checked((ulong)session.StartedAtUtc.UtcDateTime.ToFileTimeUtc()));
        BinaryPrimitives.WriteUInt64LittleEndian(request.AsSpan(16), minimum);
        BinaryPrimitives.WriteUInt64LittleEndian(request.AsSpan(24), maximum);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(32), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(36), operation);
        var response = Ioctl(Code(0x813, WriteAccess), request, request.Length);
        Validate(response, request.Length);
        if (BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(4)) != session.ProcessId ||
            BinaryPrimitives.ReadUInt64LittleEndian(response.AsSpan(8)) != BinaryPrimitives.ReadUInt64LittleEndian(request.AsSpan(8)) ||
            BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(36)) != operation)
            throw new IOException("Resident-memory response belongs to another client or operation.");
        var status = BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(40));
        if (status < 0) throw new IOException($"Resident-memory budget rejected: NTSTATUS 0x{status:X8}.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessWorkingSetSizeEx(SafeProcessHandle process, out UIntPtr minimum, out UIntPtr maximum, out uint flags);

    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static extern SafeProcessHandle OpenWorkingSetProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", EntryPoint = "GetProcessTimes", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWorkingSetProcessTimes(SafeProcessHandle process, out long birth, out long exit, out long kernel, out long user);
}

public sealed record WorkingSetBounds(ulong MinimumBytes, ulong MaximumBytes, uint Flags);
