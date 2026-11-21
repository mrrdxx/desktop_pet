namespace DesktopPet.Core.Windows;

/// <summary>
/// P5A-001：窗口栖息落位计算（纯逻辑）。
/// 以目标的可见外框（DWMWA_EXTENDED_FRAME_BOUNDS）上缘为栖息面：
/// 宠物脚底中心落在外框上缘中点，宠物窗口从该点向上延伸。
/// 坐标为虚拟桌面物理像素（允许负值）；本类不做任何 Win32 调用或
/// DIP 换算——物理像素到宠物窗口 DIP 的换算在 App 边界完成。
/// 见 PRODUCT R-07 与 ARCHITECTURE §9。
/// </summary>
public static class WindowPerchLandingPolicy
{
    /// <summary>
    /// 计算目标可见外框上缘中点的落位脚底坐标。
    /// 目标为 null 或外框退化（宽/高非正）时返回 false 且不写 out 参数。
    /// </summary>
    public static bool TryComputeLanding(
        ExternalWindowSnapshot? target,
        out double footCenterX,
        out double footCenterY)
    {
        footCenterX = 0;
        footCenterY = 0;
        if (target is null)
            return false;
        if (target.BoundsRight <= target.BoundsLeft || target.BoundsBottom <= target.BoundsTop)
            return false;

        footCenterX = (target.BoundsLeft + target.BoundsRight) / 2.0;
        footCenterY = target.BoundsTop;
        return true;
    }

    /// <summary>
    /// P5A-001 R1 F-01：带宠物尺寸与可见区域的落位计算（初选与跟随共用）。
    /// 脚底中心仍为可见外框上缘中点，但要求宠物完整矩形
    /// （[footX-petWidthPx/2, footX+petWidthPx/2] × [footY-petHeightPx, footY]）
    /// 完全位于可见区域内；容不下（如最大化后目标上沿为 0，宠物出屏）
    /// 返回 false 且不写 out 参数。petWidthPx/petHeightPx 与可见区域
    /// 坐标均为物理像素。
    /// </summary>
    public static bool TryComputeLanding(
        ExternalWindowSnapshot? target,
        double petWidthPx,
        double petHeightPx,
        double visibleLeft,
        double visibleTop,
        double visibleRight,
        double visibleBottom,
        out double footCenterX,
        out double footCenterY)
    {
        footCenterX = 0;
        footCenterY = 0;
        if (petWidthPx <= 0 || petHeightPx <= 0)
            return false;
        if (visibleRight <= visibleLeft || visibleBottom <= visibleTop)
            return false;
        if (!TryComputeLanding(target, out footCenterX, out footCenterY))
            return false;
        var petLeft = footCenterX - petWidthPx / 2.0;
        var petRight = footCenterX + petWidthPx / 2.0;
        var petTop = footCenterY - petHeightPx;
        if (petLeft < visibleLeft || petRight > visibleRight)
            return false;
        if (petTop < visibleTop)
            return false;
        return true;
    }

    /// <summary>
    /// 判断宠物当前脚底坐标是否已落位在目标上缘（容差为物理像素）。
    /// 用于落位与跟随的一致性断言；纯比较，无 I/O。
    /// </summary>
    public static bool IsAtLanding(
        double footCenterX,
        double footCenterY,
        ExternalWindowSnapshot target,
        double tolerancePx)
    {
        if (!TryComputeLanding(target, out var landingX, out var landingY))
            return false;
        var dx = footCenterX - landingX;
        var dy = footCenterY - landingY;
        return dx * dx + dy * dy <= tolerancePx * tolerancePx;
    }
}
