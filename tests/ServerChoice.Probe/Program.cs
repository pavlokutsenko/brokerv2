using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args.Length != 3 || !int.TryParse(args[0], out var pid) || !long.TryParse(args[1], out var birthTicks))
    throw new ArgumentException("PID, UTC birth ticks, and report path required");
var report = Path.GetFullPath(args[2]);
void Write(string stage, int status = 0, int serverId = -1, int calls = 0, ulong owner = 0) =>
    File.WriteAllText(report, JsonSerializer.Serialize(new { stage, pid, birthTicks, status, serverId, calls,
        owner = $"0x{owner:X}", capturedAt = DateTimeOffset.UtcNow }));
using var target = Process.GetProcessById(pid);
if (target.StartTime.ToUniversalTime().Ticks != birthTicks) throw new InvalidOperationException("PID birth mismatch");
using var mapping = MemoryMappedFile.CreateNew($@"Local\PriceCheckServerChoice_{pid}", 24);
using var view = mapping.CreateViewAccessor();
view.Write(0, 0x50435343u);
view.Write(4, 0);
view.Write(8, -1);
view.Write(12, 0);
var dll = Path.Combine(AppContext.BaseDirectory, "PriceCheck.ServerChoiceProbe.dll");
var module = Native.LoadLibraryW(dll);
if (module == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibrary");
var procedure = Native.GetProcAddress(module, "PriceCheckHookProc");
if (procedure == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcAddress");
nint window = 0;
long largest = 0;
Native.EnumWindows((handle, _) => {
    if (!Native.IsWindowVisible(handle)) return true;
    Native.GetWindowThreadProcessId(handle, out var owner);
    if (owner != pid || !Native.GetWindowRect(handle, out var rect)) return true;
    var area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
    if (area > largest) { largest = area; window = handle; }
    return true;
}, 0);
if (window == 0) throw new InvalidOperationException("No game window");
var thread = Native.GetWindowThreadProcessId(window, out _);
var hook = Native.SetWindowsHookExW(3, procedure, module, thread);
if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx");
try {
    Native.PostMessageW(window, 0, 0, 0);
    var deadline = DateTimeOffset.UtcNow.AddMinutes(10);
    while (DateTimeOffset.UtcNow < deadline) {
        var status = view.ReadInt32(4);
        var serverId = view.ReadInt32(8);
        var calls = view.ReadInt32(12);
        var owner = view.ReadUInt64(16);
        if (status < 0) { Write("error", status, serverId, calls, owner); break; }
        if (calls > 0) { Write("captured", status, serverId, calls, owner); break; }
        if (status == 1) Write("ready", status, serverId, calls, owner);
        if (target.HasExited) { Write("client_exited", status, serverId, calls, owner); break; }
        await Task.Delay(250);
    }
} finally {
    Native.UnhookWindowsHookEx(hook);
    Native.FreeLibrary(module);
}

static class Native {
    public delegate bool EnumCallback(nint window, nint state);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCallback callback, nint state);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowsHookExW(int kind, nint procedure, nint module, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool PostMessageW(nint window, uint msg, nint wparam, nint lparam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern nint LoadLibraryW(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] public static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll")] public static extern bool FreeLibrary(nint module);
}
