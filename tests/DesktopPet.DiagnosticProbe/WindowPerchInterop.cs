using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using DesktopPet.Core.Windows;

namespace DesktopPet.DiagnosticProbe;

/// <summary>
/// P5A-PLAN：window-perch 场景的 Win32 只读采集层。
/// 全部为只读查询（枚举/属性/命中测试）；唯一的状态变更是场景开头的
/// 进程 DPI 感知设置（SetProcessDpiAwarenessContext，PMv2），
/// 不改任何第三方窗口、不注入、不要求管理员权限。
/// 坐标统一为虚拟桌面物理像素（允许负值）。
/// </summary>
internal static class WindowPerchInterop
{
    // ---- 结构 ----
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    // ---- 常量 ----
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x0000_0080;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 13;
    internal const uint MONITORINFOF_PRIMARY = 1;
    private static readonly IntPtr PMv2Context = new(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    // ---- P/Invoke ----
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc proc, IntPtr lParam);
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowTextW(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder sb, int maxCount);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetClassNameW(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder sb, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags); // GA_ROOT=2
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEXW info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags); // MONITOR_DEFAULTTONEAREST=2

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int cbRect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int cbValue);

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value); // DPI_AWARENESS_*：0=Unaware 1=System 2=PerMonitor

    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY); // MDT_EFFECTIVE_DPI=0

    /// <summary>尝试把本进程设为 Per-Monitor V2。返回实际 awareness（0=Unaware 1=System 2=PerMonitor）。</summary>
    public static int TrySetPerMonitorV2Awareness()
    {
        SetProcessDpiAwarenessContext(PMv2Context);
        return GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
    }

    /// <summary>采集一个顶层窗口快照（物理像素）。窗口已销毁时返回 null。</summary>
    public static ExternalWindowSnapshot? SnapshotWindow(IntPtr hwnd, Func<uint, string> processNameResolver)
    {
        if (!IsWindow(hwnd)) return null;

        var titleSb = new StringBuilder(512);
        GetWindowTextW(hwnd, titleSb, 512);
        var classSb = new StringBuilder(256);
        GetClassNameW(hwnd, classSb, 256);
        _ = GetWindowThreadProcessId(hwnd, out uint pid);

        var exStyle = GetWindowLongPtrW(hwnd, GWL_EXSTYLE) & 0xFFFFFFFF;

        var bounds = GetVisibleFrameBounds(hwnd);
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int));

        return new ExternalWindowSnapshot(
            HwndValue: hwnd.ToInt64(),
            Title: titleSb.ToString(),
            ClassName: classSb.ToString(),
            ProcessId: pid,
            ProcessName: processNameResolver(pid),
            IsWindowAlive: true,
            IsVisible: IsWindowVisible(hwnd),
            IsMinimized: IsIconic(hwnd),
            IsCloaked: cloaked != 0,
            IsToolWindow: (exStyle & WS_EX_TOOLWINDOW) != 0,
            BoundsLeft: bounds.Left,
            BoundsTop: bounds.Top,
            BoundsRight: bounds.Right,
            BoundsBottom: bounds.Bottom);
    }

    /// <summary>可见外框：优先 DWMWA_EXTENDED_FRAME_BOUNDS（不含不可见边框），失败回退 GetWindowRect。</summary>
    public static RECT GetVisibleFrameBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>()) == 0)
            return frame;
        return GetWindowRect(hwnd, out RECT rect) ? rect : default;
    }

    /// <summary>枚举全部顶层窗口快照（Z 序自顶向下）。</summary>
    public static List<ExternalWindowSnapshot> EnumerateTopLevelWindows(Func<uint, string> processNameResolver)
    {
        var list = new List<ExternalWindowSnapshot>();
        EnumWindows((hwnd, _) =>
        {
            var snap = SnapshotWindow(hwnd, processNameResolver);
            if (snap is not null) list.Add(snap);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>按句柄值取原始 HWND（调用方保证仍在跟踪该句柄）。</summary>
    public static IntPtr HwndFromValue(long value) => new(value);

    /// <summary>IsWindow 检查（不产生快照分配）。</summary>
    public static bool IsWindowAlive(long hwndValue) => IsWindow(new IntPtr(hwndValue));

    /// <summary>
    /// 候选栖息面遮挡采样：在外框上缘（top+2px）等距取 sampleCount 个点，
    /// WindowFromPoint 命中根窗口等于目标即未遮挡。返回可见比例 0..1 与遮挡窗口标题列表。
    /// </summary>
    public static (double VisibleFraction, List<string> Occluders) SampleTopEdgeOcclusion(
        ExternalWindowSnapshot target, int sampleCount, Func<long, string> titleResolver)
    {
        var hwnd = new IntPtr(target.HwndValue);
        var y = target.BoundsTop + 2;
        var width = target.BoundsRight - target.BoundsLeft;
        var visible = 0;
        var occluderHwnds = new HashSet<long>();
        for (var i = 0; i < sampleCount; i++)
        {
            var x = target.BoundsLeft + (int)Math.Round(width * (i + 0.5) / sampleCount);
            var hit = WindowFromPoint(new POINT { X = x, Y = y });
            var root = GetAncestor(hit, 2);
            if (root == hwnd)
            {
                visible++;
                continue;
            }
            if (root != IntPtr.Zero) occluderHwnds.Add(root.ToInt64());
        }
        var occluders = new List<string>();
        foreach (var h in occluderHwnds)
        {
            var sb = new StringBuilder(256);
            GetWindowTextW(new IntPtr(h), sb, 256);
            occluders.Add(sb.ToString());
        }
        return ((double)visible / sampleCount, occluders);
    }

    /// <summary>枚举显示器布局与有效 DPI（物理像素）。</summary>
    public static List<MonitorLayout> EnumerateMonitors()
    {
        var list = new List<MonitorLayout>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW { cbSize = Marshal.SizeOf<MONITORINFOEXW>() };
            if (GetMonitorInfo(hMonitor, ref info))
            {
                var dpiOk = GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY) == 0;
                list.Add(new MonitorLayout(
                    info.szDevice,
                    info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom,
                    info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom,
                    dpiOk ? dpiX : 0, dpiOk ? dpiY : 0,
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>窗口所在显示器的 DPI（0 表示查询失败）。</summary>
    public static uint WindowDpi(long hwndValue)
    {
        var monitor = MonitorFromWindow(new IntPtr(hwndValue), 2);
        if (monitor == IntPtr.Zero) return 0;
        return GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 ? dpiX : 0;
    }

    internal sealed record MonitorLayout(
        string Device,
        int MonitorLeft, int MonitorTop, int MonitorRight, int MonitorBottom,
        int WorkLeft, int WorkTop, int WorkRight, int WorkBottom,
        uint DpiX, uint DpiY,
        bool IsPrimary);
}
