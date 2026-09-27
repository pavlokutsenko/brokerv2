using System.Runtime.InteropServices;
namespace PriceCheck.Collector.Services;
public sealed partial class ClientProcessService
{
    private static long LargestVisibleWindowArea(int pid)
    {
        long largest = 0;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)pid || !GetWindowRect(window, out var rect)) return true;
            var width = Math.Max(0, rect.Right - rect.Left);
            var height = Math.Max(0, rect.Bottom - rect.Top);
            largest = Math.Max(largest, (long)width * height);
            return true;
        }, IntPtr.Zero);
        return largest;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
}
