using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PriceCheck.Collector.Services;

// Install the kernel traffic policy before allowing any code in the launcher to run.
internal sealed class SuspendedClientProcess : IDisposable
{
    private ProcessInfo _native;
    private bool _resumed;
    public Process Process { get; }
    internal nint NativeHandle => _native.Process;
    public SuspendedClientProcess(ProcessStartInfo start)
    {
        var environment = string.Join('\0', start.Environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Where(pair => pair.Value is not null).Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
        var pointer = Marshal.StringToHGlobalUni(environment);
        try
        {
            var info = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
            var arguments = start.ArgumentList.Count > 0
                ? string.Join(' ', start.ArgumentList.Select(Quote)) : start.Arguments;
            if (!CreateProcessW(start.FileName, new StringBuilder(Quote(start.FileName) + " " + arguments), 0, 0, false,
                0x4 | 0x400 | (start.CreateNoWindow ? 0x8000000u : 0), pointer, start.WorkingDirectory, ref info, out _native))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the protected process.");
            Process = Process.GetProcessById((int)_native.Pid);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    private static string Quote(string value) => "\"" +
        System.Text.RegularExpressions.Regex.Replace(value, @"(\\*)(""|$)",
            match => new string('\\', match.Groups[1].Length * 2) +
                (match.Groups[2].Value == "\"" ? "\\\"" : "")) + "\"";
    public void Resume()
    {
        if (ResumeThread(_native.Thread) == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the protected process.");
        _resumed = true;
    }
    public void Dispose()
    {
        if (!_resumed) TerminateProcess(_native.Process, 1);
        CloseHandle(_native.Thread); CloseHandle(_native.Process); Process.Dispose();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
        public short Show, ReservedSize;
        public nint ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { public nint Process, Thread; public uint Pid, Tid; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string app, StringBuilder command, nint processAttributes,
        nint threadAttributes, bool inherit, uint flags, nint environment, string directory,
        ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(nint process, uint code);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
