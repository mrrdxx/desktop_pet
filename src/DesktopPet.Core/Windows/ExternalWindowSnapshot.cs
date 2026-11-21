namespace DesktopPet.Core.Windows;

/// <summary>
/// P5A-PLAN：外部顶层窗口只读快照。探针在 Win32 边界采集后传入纯逻辑，
/// 本类型不做任何坐标换算或 Win32 调用。
/// 坐标为虚拟桌面物理像素（可见外框，DWMWA_EXTENDED_FRAME_BOUNDS）；
/// 允许负坐标（左侧/上方扩展屏）。
/// </summary>
public sealed record ExternalWindowSnapshot(
    long HwndValue,
    string Title,
    string ClassName,
    uint ProcessId,
    string ProcessName,
    bool IsWindowAlive,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    bool IsToolWindow,
    int BoundsLeft,
    int BoundsTop,
    int BoundsRight,
    int BoundsBottom)
{
    /// <summary>句柄数值相同（不等于同一窗口，句柄可复用）。</summary>
    public bool HasSameHandle(ExternalWindowSnapshot other) => HwndValue == other.HwndValue;

    /// <summary>内容身份相同：窗口类 + 进程 ID。标题会自然变化（如另存为后），
    /// 不参与身份判定，只用于选择与资格条件；句柄数值被其他窗口复用时此判断为 false。</summary>
    public bool HasSameIdentity(ExternalWindowSnapshot other) =>
        ClassName == other.ClassName
        && ProcessId == other.ProcessId;
}
