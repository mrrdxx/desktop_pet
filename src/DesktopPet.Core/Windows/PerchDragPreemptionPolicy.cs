namespace DesktopPet.Core.Windows;

/// <summary>
/// P5A-001：拖动抢占后的重绑策略（纯逻辑）。
/// 用户开始拖动宠物时立即交回位置控制并解除栖息绑定；松手后不得被
/// 拉回旧目标——被拖动抢占解除的目标句柄不得被自动重选。
/// 重新栖息只能来自一次明确的显式新选择（输入即选择，不依赖轮询）。
/// </summary>
public static class PerchDragPreemptionPolicy
{
    /// <summary>
    /// 拖动抢占取消后，候选句柄是否允许被自动重选（轮询/枚举自动绑定）。
    /// 候选与被拖动取消的句柄相同 → 拒绝自动重选（防止松手后被拉回旧目标）。
    /// </summary>
    public static bool ShouldAutoRebind(long? dragCancelledHwndValue, long candidateHwndValue)
        => dragCancelledHwndValue != candidateHwndValue;

    /// <summary>
    /// 拖动抢占取消后，一次明确的显式新选择是否允许绑定该候选。
    /// 显式选择是独立的新输入：即使与被取消句柄相同也允许（初版不依赖
    /// 句柄数值之外的窗口身份，显式选择由调用方保证语义）。
    /// </summary>
    public static bool ShouldAllowExplicitSelection(long? dragCancelledHwndValue, long candidateHwndValue, bool isExplicitNewSelection)
        => isExplicitNewSelection || ShouldAutoRebind(dragCancelledHwndValue, candidateHwndValue);
}
