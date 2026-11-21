namespace DesktopPet.Core.Windows;

/// <summary>探针在 Cleared/NoTarget 状态下对候选窗口的处置动作。</summary>
public enum PerchPollAction
{
    /// <summary>不处理（无候选或拒绝重绑）。</summary>
    None = 0,
    /// <summary>经 TryReacquire 自动重验（最小化/隐藏还原、或新句柄的明确新选择）。</summary>
    TryReacquire = 1,
    /// <summary>同值句柄被拒绝自动恢复：持续轮询也不得经旧筛选条件重新绑定。</summary>
    RejectedSameHandle = 2,
}

/// <summary>
/// P5A-PLAN R2 F-02：探针轮询的选择决策（纯逻辑，供探针与确定性单测共用）。
/// WindowGone/HandleReused 后，同数值句柄即使身份字段全部一致也不得自动恢复；
/// 探针不得再用原筛选条件对被拒候选做 Reset+Select 回退（R2 指出那不是独立的
/// 新选择输入）。重新绑定只能来自明确的显式新选择（重开探针指定新目标）。
/// </summary>
public static class WindowPerchSelectionPolicy
{
    public static PerchPollAction DecideCleared(
        PerchTargetClearReason clearReason,
        bool candidateMatchesClearedHandle)
    {
        if (candidateMatchesClearedHandle &&
            clearReason is PerchTargetClearReason.WindowGone or PerchTargetClearReason.HandleReused)
            return PerchPollAction.RejectedSameHandle;
        return PerchPollAction.TryReacquire;
    }
}
