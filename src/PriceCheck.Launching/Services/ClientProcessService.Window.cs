using System.Runtime.InteropServices;
using PriceCheck.Collector.Contracts;
using PriceCheck.Contracts;
using PriceCheck.Windows;

namespace PriceCheck.Collector.Services;
public sealed partial class ClientProcessService
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint MoveWithoutActivation = 0x0001 | 0x0004 | 0x0010 | 0x0200;

    private static long LargestVisibleWindowArea(int pid)
    {
        FindLargestVisibleWindow(pid, out var area);
        return area;
    }

    public bool TryPlaceGameWindow(ClientSession session, GameWindowCorner corner)
    {
        if (!ClientProcessIdentity.IsCurrent(session)) return false;
        var window = FindLargestVisibleWindow(session.ProcessId, out var area);
        if (window == nint.Zero || area < 800L * 600L || !GetWindowRect(window, out var rect)) return false;
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info)) return false;
        var work = info.Work;
        if (work.Right <= work.Left || work.Bottom <= work.Top) return false;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var left = corner is GameWindowCorner.TopRight or GameWindowCorner.BottomRight
            ? Math.Max(work.Left, work.Right - width) : work.Left;
        var top = corner is GameWindowCorner.BottomLeft or GameWindowCorner.BottomRight
            ? Math.Max(work.Top, work.Bottom - height) : work.Top;
        return ClientProcessIdentity.IsCurrent(session) &&
            SetWindowPos(window, nint.Zero, left, top, 0, 0, MoveWithoutActivation);
    }

    private static nint FindLargestVisibleWindow(int pid, out long largest)
    {
        nint largestWindow = nint.Zero;
        long largestArea = 0;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)pid || !GetWindowRect(window, out var rect)) return true;
            var width = Math.Max(0, rect.Right - rect.Left);
            var height = Math.Max(0, rect.Bottom - rect.Top);
            var area = (long)width * height;
            if (area > largestArea) { largestArea = area; largestWindow = window; }
            return true;
        }, IntPtr.Zero);
        largest = largestArea;
        return largestWindow;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public WindowRect Monitor;
        public WindowRect Work;
        public uint Flags;
    }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
