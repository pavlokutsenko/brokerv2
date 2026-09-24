using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PriceCheck.Collector.Services;

// A thread-local Windows message hook makes the OS load our agent into the
// client's GUI process. The hook is removed once the agent reports readiness.
internal static class ClientAgentHookLoader
{
    private const int WhGetMessage = 3;
    private const uint WmNull = 0;

    public static async Task<HookLease> InstallAsync(int pid, string agentPath, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var process = Process.GetProcessById(pid);
        if (process.HasExited) throw new InvalidOperationException("The client exited before agent loading.");
            }
            catch (ArgumentException)
            {
            throw new InvalidOperationException("The client exited before agent loading.");
            }

            var window = FindWindow(pid);
            if (window != nint.Zero)
            {
                var threadId = GetWindowThreadProcessId(window, out _);
                if (threadId == 0)
                {
                    await Task.Delay(250, cancellationToken);
                    continue;
                }
                var module = LoadLibraryW(agentPath);
            if (module == nint.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not load the launch agent.");
                try
                {
                    var hookProcedure = GetProcAddress(module, "PriceCheckHookProc");
                    if (hookProcedure == nint.Zero)
                throw new InvalidOperationException("The agent has no window hook procedure.");
                    var hook = SetWindowsHookExW(WhGetMessage, hookProcedure, module, threadId);
                    if (hook == nint.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach the agent to the client window.");
                    var lease = new HookLease(hook, module, window);
                    lease.Pulse();
                    return lease;
                }
                catch
                {
                    FreeLibrary(module);
                    throw;
                }
            }
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("The client window did not appear within 90 seconds; the HWID agent was not loaded.");
    }

    private static nint FindWindow(int pid)
    {
        nint largestWindow = nint.Zero;
        long largestArea = 0;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)pid || !GetWindowRect(window, out var rect)) return true;
            var area = (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
            if (area > largestArea) { largestArea = area; largestWindow = window; }
            return true;
        }, nint.Zero);
        return largestWindow;
    }

    internal sealed class HookLease(nint hook, nint module, nint window) : IDisposable
    {
        private nint _hook = hook;
        private nint _module = module;
        public void Pulse() => PostMessageW(window, WmNull, nint.Zero, nint.Zero);

        public void Dispose()
        {
            if (_hook != nint.Zero) { UnhookWindowsHookEx(_hook); _hook = nint.Zero; }
            if (_module != nint.Zero) { FreeLibrary(_module); _module = nint.Zero; }
        }
    }

    private delegate bool EnumWindowsCallback(nint window, nint state);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out WindowRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessageW(nint window, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int kind, nint procedure, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryW(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeLibrary(nint module);
}
