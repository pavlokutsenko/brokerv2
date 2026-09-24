using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PriceCheck.Collector.Services;

// Experimental root-process loader. The root starts suspended so the child
// creation hook is installed before it launches lu4.bin.
internal static class EarlyClientAgentLoader
{
    private const uint CreateSuspended = 0x4;
    private const uint UnicodeEnvironment = 0x400;

    public static Process Start(ProcessStartInfo start, string agentPath, CancellationToken token)
    {
        start.Environment["PRICECHECK_EARLY_CHILD_INJECT"] = "1";
        var environment = BuildEnvironment(start.Environment);
        var environmentPointer = Marshal.StringToHGlobalUni(environment);
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        var command = new StringBuilder('"' + start.FileName + '"');
        try
        {
            if (!CreateProcessW(start.FileName, command, 0, 0, false,
                    CreateSuspended | UnicodeEnvironment, environmentPointer,
                    start.WorkingDirectory, ref startup, out var created))
                throw new InvalidOperationException($"CreateProcessW: {Marshal.GetLastWin32Error()}");
            try
            {
                Inject(created.Process, agentPath, token);
                WaitForReady(created.ProcessId, token);
                if (ResumeThread(created.Thread) == uint.MaxValue)
                    throw new InvalidOperationException($"ResumeThread: {Marshal.GetLastWin32Error()}");
                return Process.GetProcessById(created.ProcessId);
            }
            catch
            {
                TerminateProcess(created.Process, 1);
                throw;
            }
            finally
            {
                CloseHandle(created.Thread);
                CloseHandle(created.Process);
            }
        }
        finally { Marshal.FreeHGlobal(environmentPointer); }
    }

    private static string BuildEnvironment(IDictionary<string, string?> variables) =>
        string.Join('\0', variables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key + "=" + item.Value)) + "\0\0";

    private static void Inject(nint process, string agentPath, CancellationToken token)
    {
        var payload = Encoding.Unicode.GetBytes(agentPath + '\0');
        var remote = VirtualAllocEx(process, 0, (nuint)payload.Length, 0x3000, 0x04);
        if (remote == 0) throw new InvalidOperationException($"VirtualAllocEx: {Marshal.GetLastWin32Error()}");
        try
        {
            if (!WriteProcessMemory(process, remote, payload, (nuint)payload.Length, out var written) ||
                written != (nuint)payload.Length)
                throw new InvalidOperationException($"WriteProcessMemory: {Marshal.GetLastWin32Error()}");
            var loader = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
            if (loader == 0) throw new InvalidOperationException("LoadLibraryW unavailable");
            var thread = CreateRemoteThread(process, 0, 0, loader, remote, 0, out _);
            if (thread == 0) throw new InvalidOperationException($"CreateRemoteThread: {Marshal.GetLastWin32Error()}");
            try
            {
                var wait = WaitForSingleObject(thread, 10_000);
                token.ThrowIfCancellationRequested();
                if (wait != 0 || !GetExitCodeThread(thread, out var result) || result == 0)
            throw new InvalidOperationException("Early agent did not load into the launcher process.");
            }
            finally { CloseHandle(thread); }
        }
        finally { VirtualFreeEx(process, remote, 0, 0x8000); }
    }

    private static void WaitForReady(int pid, CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (Environment.TickCount64 < deadline)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var ready = EventWaitHandle.OpenExisting($@"Local\PriceCheckAgentReady_{pid}");
                if (ready.WaitOne(0)) return;
            }
            catch (WaitHandleCannotBeOpenedException) { }
            Thread.Sleep(50);
        }
            throw new TimeoutException("Early agent did not report readiness in the launcher process.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, Width, Height, CharactersX, CharactersY, Fill, Flags;
        public short ShowWindow, Reserved2;
        public nint ReservedPointer, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder command,
        nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint flags, nint environment, string directory, ref StartupInfo startup,
        out ProcessInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint VirtualAllocEx(nint process, nint address, nuint size, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(nint process, nint address, nuint size, uint freeType);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(nint process, nint address, byte[] data, nuint size, out nuint written);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateRemoteThread(nint process, nint attributes, nuint stack,
        nint entry, nint parameter, uint flags, out uint threadId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeThread(nint thread, out uint result);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
