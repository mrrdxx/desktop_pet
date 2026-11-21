namespace DesktopPet.Core.Windows;

/// <summary>
/// P5A-001：落脚处遮挡脱离判定（纯逻辑）。
/// 初版遮挡策略为脱离：落位处（外框上缘）被遮挡即解除绑定，
/// 宠物留在当前位置（回到可见区域由调用方处理）。
/// 遮挡比例由调用方采样（如 WindowFromPoint 上缘等距点）；
/// 本类只做阈值判定，无 I/O。
/// </summary>
public static class WindowPerchOcclusionPolicy
{
    /// <summary>默认脱离阈值：上缘采样点只要有任一被遮挡（fraction &lt; 1）即脱离。</summary>
    public const double DefaultMinimumVisibleFraction = 1.0;

    /// <summary>
    /// 判断当前遮挡比例是否应脱离绑定。
    /// visibleFraction 为落位处可见比例 0..1；小于阈值即脱离。
    /// </summary>
    public static bool ShouldDetach(double visibleFraction, double minimumVisibleFraction = DefaultMinimumVisibleFraction)
        => visibleFraction < minimumVisibleFraction;
}
