using System.Runtime.InteropServices;
using DesktopPet.Core.Geometry;

namespace DesktopPet.App.Platform;

/// <summary>
/// Win32 monitor enumeration and DPI queries.
/// This is the Platform boundary: all Win32 calls live here,
/// Core never calls Win32 directly.
/// </summary>
internal static class MonitorInterop
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
        ref RECT lprcMonitor, IntPtr dwData);

    // GetDpiForMonitor is exported by Shcore, not User32. Return value is HRESULT.
    // Used for enumerating per-monitor DPI metadata (HMONITOR → DPI).
    // Per MS docs, per-monitor DPI aware threads should use GetDpiForWindow for
    // querying a window's actual DPI context, not GetDpiForMonitor.
    [DllImport("shcore.dll", ExactSpelling = true)]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType,
        out uint dpiX, out uint dpiY);

    private const int MDT_EFFECTIVE_DPI = 0;

    // GetDpiForWindow is the recommended API for per-monitor DPI aware processes.
    // Returns the DPI for the window's actual monitor context under PMv2.
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    /// <summary>
    /// Enumerate all monitors on the virtual desktop, returning their geometry
    /// in physical pixels with DPI scale information.
    /// </summary>
    public static List<MonitorInfo> EnumerateMonitors()
    {
        var result = new List<MonitorInfo>();
        var nativeMonitors = new List<(IntPtr Handle, MONITORINFOEX Info)>();

        bool enumerated = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var info = new MONITORINFOEX
                {
                    cbSize = Marshal.SizeOf<MONITORINFOEX>()
                };

                if (GetMonitorInfoW(hMonitor, ref info))
                {
                    nativeMonitors.Add((hMonitor, info));
                }

                return true;
            }, IntPtr.Zero);

        if (!enumerated)
        {
            System.Diagnostics.Trace.TraceWarning("EnumDisplayMonitors failed: {0}", Marshal.GetLastPInvokeError());
            return result;
        }

        // Query DPI after the native callback has returned. Interop exceptions must
        // not unwind through EnumDisplayMonitors' unmanaged callback stack.
        foreach (var (handle, info) in nativeMonitors)
        {
            int hr = GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
            if (hr < 0 || dpiX == 0 || dpiY == 0)
            {
                System.Diagnostics.Trace.TraceWarning("GetDpiForMonitor failed for {0}: HRESULT=0x{1:X8}", info.szDevice, hr);
                continue;
            }

            var fullBounds = MonitorRectPx.FromLTRB(
                info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
            var workBounds = MonitorRectPx.FromLTRB(
                info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
            if (workBounds.Width <= 0 || workBounds.Height <= 0)
                continue;

            result.Add(new MonitorInfo(info.szDevice, fullBounds,
                new WorkAreaPx(workBounds), dpiX / 96.0, dpiY / 96.0));
        }

        return result;
    }

    /// <summary>
    /// Get the DPI scale for a specific window's monitor under PMv2.
    /// Uses GetDpiForWindow (recommended for per-monitor DPI aware processes)
    /// instead of GetDpiForMonitor, per Microsoft documentation.
    /// </summary>
    /// <param name="hwnd">Window handle.</param>
    /// <returns>DPI scale (1.0 = 96 DPI, 1.5 = 144 DPI, etc.), or 1.0 on failure.</returns>
    public static (double ScaleX, double ScaleY) GetWindowDpiScale(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return (1.0, 1.0);

        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
            return (1.0, 1.0);

        return (dpi / 96.0, dpi / 96.0);
    }

    public static string? GetWindowMonitorDeviceName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;
        var monitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref info) ? info.szDevice : null;
    }
}
