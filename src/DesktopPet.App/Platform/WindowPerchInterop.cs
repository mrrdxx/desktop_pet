using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using DesktopPet.Core.Windows;

namespace DesktopPet.App.Platform;

/// <summary>
/// P5A-001：窗口栖息的 Win32 只读采集层（自探针 WindowPerchInterop 提升）。
/// 全部为只读查询（枚举/属性/命中测试），不改任何第三方窗口、不注入、
/// 不要求管理员权限。坐标统一为虚拟桌面物理像素（允许负值）。
/// 见 ARCHITECTURE §9。
/// </summary>
public static class WindowPerchInterop
{
    // ---- 结构 ----
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

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
    private static readonly IntPtr PMv2Context = new(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    // ---- P/Invoke ----
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowTextW(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder sb, int maxCount);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetClassNameW(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder sb, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags); // GA_ROOT=2
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int cbRect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int cbValue);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW info);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary>尝试把本进程设为 Per-Monitor V2（探针场景在创建 WPF Application 前调用）。</summary>
    public static void TrySetPerMonitorV2Awareness()
        => SetProcessDpiAwarenessContext(PMv2Context);

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

    /// <summary>按句柄值取原始 HWND（调用方保证仍在跟踪该句柄）。</summary>
    public static IntPtr HwndFromValue(long value) => new(value);

    /// <summary>
    /// P5A-001 R1 F-01：取点所在显示器的完整边界（物理像素）。
    /// MONITOR_DEFAULTTONEAREST：点不在任何显示器内也取最近显示器。
    /// 返回 false 表示 Win32 查询失败（调用方应拒绝落位）。
    /// </summary>
    public static bool TryGetMonitorBoundsForPoint(
        int x, int y,
        out int left, out int top, out int right, out int bottom)
    {
        left = top = right = bottom = 0;
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (monitor == IntPtr.Zero)
            return false;
        var info = new MONITORINFOEXW { cbSize = Marshal.SizeOf<MONITORINFOEXW>() };
        if (!GetMonitorInfoW(monitor, ref info))
            return false;
        left = info.rcMonitor.Left;
        top = info.rcMonitor.Top;
        right = info.rcMonitor.Right;
        bottom = info.rcMonitor.Bottom;
        return true;
    }

    /// <summary>
    /// P5A-001 R1 F-04：实际落脚区遮挡采样。
    /// 采样区为宠物在目标上缘的实际落脚区 [footCenterX-petWidthPx/2,
    /// footCenterX+petWidthPx/2]（y=外框上缘 top+2px），等距 sampleCount 个点；
    /// WindowFromPoint 命中根窗口等于目标即未遮挡。只遮上缘远端
    /// （落脚区之外）不产生遮挡。返回可见比例 0..1 与遮挡窗口标题列表。
    /// </summary>
    public static (double VisibleFraction, List<string> Occluders) SampleLandingOcclusion(
        ExternalWindowSnapshot target,
        double footCenterX,
        double petWidthPx,
        int sampleCount,
        Func<long, string> titleResolver)
    {
        var hwnd = new IntPtr(target.HwndValue);
        var y = target.BoundsTop + 2;
        var half = petWidthPx / 2.0;
        var visible = 0;
        var occluderHwnds = new HashSet<long>();
        for (var i = 0; i < sampleCount; i++)
        {
            var x = (int)Math.Round(footCenterX - half + petWidthPx * (i + 0.5) / sampleCount);
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
            occluders.Add(titleResolver(h));
        return ((double)visible / sampleCount, occluders);
    }

    /// <summary>
    /// P5A-003：按光标点解析命中的外部根窗口并采集其快照（拾取模式
    /// 生产路径）。命中点无窗口、根窗口解析失败、或根窗口即宠物自身
    /// （ownHwnd）时返回 null。只读查询，不改任何第三方窗口。
    /// </summary>
    public static ExternalWindowSnapshot? SnapshotWindowFromPoint(
        int x, int y, IntPtr ownHwnd, Func<uint, string> processNameResolver)
    {
        var hit = WindowFromPoint(new POINT { X = x, Y = y });
        if (hit == IntPtr.Zero)
            return null;
        var root = GetAncestor(hit, 2 /* GA_ROOT */);
        if (root == IntPtr.Zero || root == ownHwnd)
            return null;
        return SnapshotWindow(root, processNameResolver);
    }
}
