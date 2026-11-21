namespace DesktopPet.Core.Windows;

/// <summary>目标状态。</summary>
public enum PerchTargetStatus
{
    /// <summary>尚未选择目标。</summary>
    NoTarget = 0,

    /// <summary>正在跟踪合格目标。</summary>
    Tracking = 1,

    /// <summary>目标已清除（失效/最小化/隐藏/失去资格），等待重新选择或恢复。</summary>
    Cleared = 2,
}

/// <summary>清除原因。</summary>
public enum PerchTargetClearReason
{
    None = 0,
    /// <summary>IsWindow 失效（窗口销毁）。</summary>
    WindowGone = 1,
    /// <summary>同一句柄数值被其他窗口复用（身份不匹配）。</summary>
    HandleReused = 2,
    /// <summary>目标最小化。</summary>
    Minimized = 3,
    /// <summary>目标不可见或被系统遮蔽。</summary>
    Hidden = 4,
    /// <summary>目标仍在但不再合格（如标题清空、变成工具窗口）。</summary>
    NoLongerEligible = 5,
}

/// <summary>单次观测的处理结果。</summary>
public enum PerchObservationOutcome
{
    /// <summary>与当前目标无关（未跟踪、或非目标句柄），不改变状态。</summary>
    Ignored = 0,
    /// <summary>目标仍然有效，快照已更新为最新观测。</summary>
    Updated = 1,
    /// <summary>目标失效，已清除。原因见 <see cref="PerchTargetTracker.ClearReason"/>。</summary>
    Cleared = 2,
}

/// <summary>
/// P5A-PLAN：栖息目标状态机（纯逻辑）。
/// 纪律（ARCHITECTURE §9）：
/// - 句柄数值不是窗口身份；同数值句柄必须经标题/类/进程 ID 重新验证，防止复用误绑定。
/// - 目标失效即清除；恢复或发现同数值句柄时通过 <see cref="TryReacquire"/> 重新验证。
/// - 探针/后续栖息实现负责低频轮询与事件采样，本类不做任何 I/O。
/// </summary>
public sealed class PerchTargetTracker
{
    private ExternalWindowSnapshot? _target;
    private ExternalWindowSnapshot? _lastObserved;

    public PerchTargetStatus Status { get; private set; } = PerchTargetStatus.NoTarget;
    public PerchTargetClearReason ClearReason { get; private set; } = PerchTargetClearReason.None;

    /// <summary>当前跟踪目标（仅 Tracking 状态非空）。</summary>
    public ExternalWindowSnapshot? Target => Status == PerchTargetStatus.Tracking ? _target : null;

    /// <summary>最近一次有效观测（bounds 随窗口移动更新）。清除后置空。</summary>
    public ExternalWindowSnapshot? LastObserved => Status == PerchTargetStatus.Tracking ? _lastObserved : null;

    /// <summary>被清除目标的句柄数值（仅 Cleared 状态非空；供调用方做同值候选判定）。</summary>
    public long? ClearedTargetHwndValue => Status == PerchTargetStatus.Cleared ? _target?.HwndValue : null;

    /// <summary>选择目标。要求目标合格；不合格返回 false 且不改变原状态。</summary>
    public bool Select(ExternalWindowSnapshot target, uint selfProcessId)
    {
        if (ExternalWindowEligibility.Evaluate(target, selfProcessId) != ExternalWindowExclusion.None)
            return false;
        _target = target;
        _lastObserved = target;
        Status = PerchTargetStatus.Tracking;
        ClearReason = PerchTargetClearReason.None;
        return true;
    }

    /// <summary>
    /// 观测一次目标句柄的当前快照。只处理当前目标句柄的观测；
    /// 同数值句柄身份不符视为复用并清除。
    /// </summary>
    public PerchObservationOutcome Observe(ExternalWindowSnapshot snapshot, uint selfProcessId)
    {
        if (Status != PerchTargetStatus.Tracking || _target is null || !_target.HasSameHandle(snapshot))
            return PerchObservationOutcome.Ignored;

        if (!snapshot.IsWindowAlive)
        {
            ClearCore(PerchTargetClearReason.WindowGone);
            return PerchObservationOutcome.Cleared;
        }

        if (!_target.HasSameIdentity(snapshot))
        {
            // 同一句柄数值，但标题/类/进程 ID 变化：句柄被复用，不是同一窗口。
            ClearCore(PerchTargetClearReason.HandleReused);
            return PerchObservationOutcome.Cleared;
        }

        if (snapshot.IsMinimized)
        {
            ClearCore(PerchTargetClearReason.Minimized);
            return PerchObservationOutcome.Cleared;
        }

        if (!snapshot.IsVisible || snapshot.IsCloaked)
        {
            ClearCore(PerchTargetClearReason.Hidden);
            return PerchObservationOutcome.Cleared;
        }

        var eligibility = ExternalWindowEligibility.Evaluate(snapshot, selfProcessId);
        if (eligibility != ExternalWindowExclusion.None)
        {
            ClearCore(PerchTargetClearReason.NoLongerEligible);
            return PerchObservationOutcome.Cleared;
        }

        _lastObserved = snapshot;
        return PerchObservationOutcome.Updated;
    }

    /// <summary>
    /// 恢复或发现同一（或同数值）句柄时重新验证目标：
    /// - 快照句柄与被清除目标相同 → 仅允许"窗口未销毁"的恢复（最小化/隐藏后还原），
    ///   要求身份一致且当前合格；若清除原因是 <see cref="PerchTargetClearReason.WindowGone"/>
    ///   或 <see cref="PerchTargetClearReason.HandleReused"/>，即使 HWND、PID 和类名都相同也拒绝——
    ///   窗口销毁后操作系统可能把同数值句柄分配给新窗口，身份判定无法区分，
    ///   必须经 <see cref="Reset"/> + <see cref="Select"/> 的明确新目标选择才能重新取得；
    /// - 快照为其他句柄 → 必须完全合格；作为新窗口恢复跟踪（调用方保证这是明确的新选择）。
    /// 验证失败返回 false 且不改变状态。
    /// </summary>
    public bool TryReacquire(ExternalWindowSnapshot snapshot, uint selfProcessId)
    {
        if (_target is not null && _target.HasSameHandle(snapshot))
        {
            // 窗口已销毁/句柄已被复用：同值句柄不得自动恢复（即使身份字段相同）。
            if (ClearReason is PerchTargetClearReason.WindowGone or PerchTargetClearReason.HandleReused)
                return false;

            // 同数值句柄且窗口未销毁：只有身份匹配（同一窗口恢复，如从任务栏还原）才允许直接恢复。
            if (!_target.HasSameIdentity(snapshot))
                return false;
        }

        if (ExternalWindowEligibility.Evaluate(snapshot, selfProcessId) != ExternalWindowExclusion.None)
            return false;

        _target = snapshot;
        _lastObserved = snapshot;
        Status = PerchTargetStatus.Tracking;
        ClearReason = PerchTargetClearReason.None;
        return true;
    }

    /// <summary>丢弃当前目标（含被清除目标的记忆），回到 NoTarget。</summary>
    public void Reset()
    {
        _target = null;
        _lastObserved = null;
        Status = PerchTargetStatus.NoTarget;
        ClearReason = PerchTargetClearReason.None;
    }

    private void ClearCore(PerchTargetClearReason reason)
    {
        // 保留 _target（含身份）用于清除后的复用判定；LastObserved/Target 不再暴露。
        _lastObserved = null;
        Status = PerchTargetStatus.Cleared;
        ClearReason = reason;
    }
}
