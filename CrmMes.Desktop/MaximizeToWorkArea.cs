using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CrmMes.Desktop;

/// <summary>A window with WindowStyle="None" and a custom WindowChrome maximizes to the whole monitor,
/// taskbar included: the bottom of the app (end of the sidebar, last rows of long lists) ended up hidden
/// behind the Windows taskbar. Answering WM_GETMINMAXINFO with the work area of the monitor the window
/// is on (taskbar excluded) makes maximize stop at the taskbar, on any monitor. MaxTrackSize must match
/// MaxSize — without it Windows still allows the window to grow to the full screen.</summary>
public static class MaximizeToWorkArea
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    public static void Attach(Window window)
    {
        void Hook()
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Hook();
        }
        else
        {
            window.SourceInitialized += (_, _) => Hook();
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        if (!TryGetWorkArea(hwnd, out var work, out var monitor))
        {
            return IntPtr.Zero;
        }

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMax.MaxPosition.X = work.Left - monitor.Left;
        minMax.MaxPosition.Y = work.Top - monitor.Top;
        minMax.MaxSize.X = work.Right - work.Left;
        minMax.MaxSize.Y = work.Bottom - work.Top;
        // Without MaxTrackSize, maximize still expands to the full monitor (under the taskbar).
        minMax.MaxTrackSize.X = minMax.MaxSize.X;
        minMax.MaxTrackSize.Y = minMax.MaxSize.Y;
        Marshal.StructureToPtr(minMax, lParam, fDeleteOld: true);
        handled = true;
        return IntPtr.Zero;
    }

    /// <summary>Work and monitor rectangles in physical pixels for the screen that owns <paramref name="hwnd"/>.</summary>
    internal static bool TryGetWorkArea(IntPtr hwnd, out Rect work, out Rect monitor)
    {
        work = default;
        monitor = default;
        var handle = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == IntPtr.Zero || !GetMonitorInfo(handle, ref info))
        {
            return false;
        }

        work = info.Work;
        monitor = info.Monitor;
        return work.Right > work.Left && work.Bottom > work.Top;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }
}
