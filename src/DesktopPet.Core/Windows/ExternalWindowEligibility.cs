namespace DesktopPet.Core.Windows;

/// <summary>P5A-PLAN：外部栖息候选窗口的排除原因。数值即判定优先级（小者优先）。</summary>
public enum ExternalWindowExclusion
{
    None = 0,

    /// <summary>IsWindow 已失效（窗口已销毁，或句柄从未有效）。</summary>
    DeadWindow = 1,

    /// <summary>属于本程序进程（宠物自身窗口）。</summary>
    SelfProcess = 2,

    /// <summary>已知桌面/系统壳窗口类（Progman/WorkerW/任务栏），无栖息意义。</summary>
    KnownShellWindow = 3,

    /// <summary>不可见（IsWindowVisible == false）。</summary>
    NotVisible = 4,

    /// <summary>已最小化（IsIconic）。</summary>
    Minimized = 5,

    /// <summary>被系统遮蔽（DWMWA_CLOAKED 非 0，如 UWP 挂起窗口、其他虚拟桌面窗口）。</summary>
    Cloaked = 6,

    /// <summary>工具窗口（WS_EX_TOOLWINDOW），非普通顶层窗口。</summary>
    ToolWindow = 7,

    /// <summary>标题为空（无标题的辅助窗口）。</summary>
    EmptyTitle = 8,

    /// <summary>外框宽度或高度不为正（离屏折叠或异常窗口）。</summary>
    ZeroSizeBounds = 9,
}

/// <summary>P5A-PLAN：外部栖息候选筛选规则（纯逻辑）。
/// 规则：只接受普通、可见、未最小化、有标题、外框有效的外部顶层窗口；
/// 排除本程序进程、桌面/系统壳、工具窗口与被遮蔽窗口。
/// 见 PRODUCT R-07 与 ARCHITECTURE §9。
/// </summary>
public static class ExternalWindowEligibility
{
    /// <summary>已知桌面/系统壳窗口类。仅排除确定的壳层，不枚举所有系统窗口。</summary>
    private static readonly HashSet<string> KnownShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",       // 桌面
        "WorkerW",       // 壁纸层
        "Shell_TrayWnd", // 主任务栏
        "Shell_SecondaryTrayWnd", // 副任务栏
    };

    /// <summary>评估候选窗口是否可栖息。返回 <see cref="ExternalWindowExclusion.None"/> 即合格。</summary>
    public static ExternalWindowExclusion Evaluate(ExternalWindowSnapshot candidate, uint selfProcessId)
    {
        if (!candidate.IsWindowAlive) return ExternalWindowExclusion.DeadWindow;
        if (candidate.ProcessId == selfProcessId) return ExternalWindowExclusion.SelfProcess;
        if (KnownShellClasses.Contains(candidate.ClassName)) return ExternalWindowExclusion.KnownShellWindow;
        if (!candidate.IsVisible) return ExternalWindowExclusion.NotVisible;
        if (candidate.IsMinimized) return ExternalWindowExclusion.Minimized;
        if (candidate.IsCloaked) return ExternalWindowExclusion.Cloaked;
        if (candidate.IsToolWindow) return ExternalWindowExclusion.ToolWindow;
        if (string.IsNullOrWhiteSpace(candidate.Title)) return ExternalWindowExclusion.EmptyTitle;
        if (candidate.BoundsRight <= candidate.BoundsLeft || candidate.BoundsBottom <= candidate.BoundsTop)
            return ExternalWindowExclusion.ZeroSizeBounds;
        return ExternalWindowExclusion.None;
    }
}
