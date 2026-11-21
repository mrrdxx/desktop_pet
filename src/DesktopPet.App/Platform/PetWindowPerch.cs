using System;
using System.Collections.Generic;
using System.Diagnostics;
using DesktopPet.Core.Geometry;
using DesktopPet.Core.Windows;

namespace DesktopPet.App.Platform;

/// <summary>栖息事件（探针/测试观测用；生产暂无 UI 依赖）。</summary>
public sealed record PetPerchEvent(
    string Type,
    string? Reason = null,
    double? Fraction = null,
    long? TargetHwnd = null,
    int[]? TargetBounds = null,
    double FootX = 0,
    double FootY = 0);

/// <summary>
/// P5A-001 R1：一次落位/跟随尝试的结果。
/// Landed=已提交并报告；NoLanding=完整宠物矩形在可见区域内容不下
/// （如最大化后上沿容不下）；Occluded=实际落脚区被遮挡（提交前拒绝）；
/// WriteRejected=位置被更高优先级行为占用（守卫拒绝，不报不消费）。
/// </summary>
public enum PerchLandingResult
{
    Landed,
    NoLanding,
    Occluded,
    WriteRejected,
}

/// <summary>
/// P5A-001：单宠窗口栖息控制器。
/// 职责（ARCHITECTURE §5/§9）：
/// - 显式选择一个合格外部目标后，把宠物落位到目标可见外框上缘（一次性移动）；
/// - 轮询目标（只读观测），目标外框变化时跟随（受守卫的位置写入，
///   仅在非拖动/非抚摸/非暂停/非物理/非探头/非社交时写入宠物 Left/Top）；
/// - 目标失效（关闭/最小化/隐藏/失去资格）或实际落脚区被遮挡时脱离
///   （宠物留在当前位置）；提交前先校验落位可见性与落脚区遮挡，
///   校验不过不写入（R1 F-01/F-04）；
/// - 用户开始拖动时立即被 <see cref="CancelForDrag"/> 抢占：解除绑定、
///   不恢复位置，被抢占取消的句柄不得被自动重选（松手后不回拉旧目标）。
/// 绑定只在内存：句柄数值不作永久身份，窗口销毁/复用由 Core 状态机判定。
/// 仅在 UI 线程使用；栖息位置不写入存档（退出保存前恢复栖息前位置）。
/// </summary>
public sealed class PetWindowPerch
{
    private const int OcclusionSampleCount = 9;

    private readonly MainWindow _owner;
    private readonly PerchTargetTracker _tracker = new();
    private readonly PerchUpdateGate _updateGate = new();
    private readonly Dictionary<uint, string> _pidNameCache = new();
    private readonly System.Windows.Threading.DispatcherTimer _timer;

    private long? _dragCancelledHwndValue;

    // P5A-001 R1 F-03：栖息前位置（DIP）；退出保存前恢复，保证存档坐标
    // 是休息位而非临时栖息位。仅在 IsActive 期间有效。
    private bool _hasPrePerchPosition;
    private double _prePerchLeft;
    private double _prePerchTop;

    // P5A-001 R1 F-04：当前落脚区（稳态遮挡检查用；上一次成功落位时记录）。
    private double _lastFootX;
    private double _lastPetWidthPx;

    /// <summary>当前是否正在栖息跟随。</summary>
    public bool IsActive { get; private set; }

    /// <summary>当前目标句柄数值（仅 IsActive 时非空）。</summary>
    public long? TargetHwndValue => IsActive ? _tracker.Target?.HwndValue : null;

    /// <summary>被拖动抢占取消的句柄数值（自动重选黑名单；显式新选择不受限）。</summary>
    public long? DragCancelledHwndValue => _dragCancelledHwndValue;

    /// <summary>栖息事件流（探针写 jsonl；生产可为空）。</summary>
    public event Action<PetPerchEvent>? PerchEvent;

    /// <summary>
    /// P5A-001 R3 F-04 测试缝：覆盖目标快照采集（返回 null 等价于句柄
    /// 已销毁）。仅测试使用；生产轮询路径除快照来源外不变。
    /// </summary>
    internal Func<long, ExternalWindowSnapshot?>? TestSnapshotSource;

    /// <summary>
    /// P5A-002 测试缝：覆盖落位遮挡采样（返回 (可见比例, 遮挡者列表)）。
    /// 伪造句柄的落脚区 WindowFromPoint 永远命中其他根窗口，无法在单测
    /// 构造"未遮挡"的 Landed 跟随；本缝只替换采样来源，落位/提交/事件
    /// 路径不变。仅测试使用。
    /// </summary>
    internal Func<ExternalWindowSnapshot, double, double, (double VisibleFraction, List<string> Occluders)>?
        TestLandingOcclusionSampler;

    public PetWindowPerch(MainWindow owner)
    {
        _owner = owner;
        _timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>
    /// 明确的显式新选择：绑定目标并立即落位到可见外框上缘。
    /// 目标不合格（或快照退化）返回 false 且不改变状态。
    /// P5A-001 R1 F-02：位置被更高优先级行为（拖动/抚摸/暂停/物理/
    /// 探头/社交）占用时拒绝初选，不报 landed。
    /// R1 F-01/F-04：完整宠物矩形容不下可见区域、或实际落脚区被遮挡
    /// 时拒绝初选（不移动、不报 landed）。
    /// </summary>
    public bool TrySelectExplicit(ExternalWindowSnapshot snapshot, uint selfProcessId)
    {
        // F-02：位置所有权守卫——无法提交时不得报告落位。
        if (!_owner.IsPerchWriteAllowed)
            return false;
        if (!PerchDragPreemptionPolicy.ShouldAllowExplicitSelection(
                _dragCancelledHwndValue, snapshot.HwndValue, isExplicitNewSelection: true))
            return false;
        if (ExternalWindowEligibility.Evaluate(snapshot, selfProcessId) != ExternalWindowExclusion.None)
            return false;

        // P5A-001 R2 F-02：已有绑定时重选——先保存旧绑定与栖息前位置，
        // 新目标选择或落位失败必须原样保留旧绑定（跟踪器/计时器/
        // IsActive 状态一致），下一轮轮询读到的仍是原目标，
        // 不得读到空目标。
        var oldTarget = _tracker.Target;
        var oldHasPrePerch = _hasPrePerchPosition;
        var oldPreLeft = _prePerchLeft;
        var oldPreTop = _prePerchTop;

        // F-03：栖息前位置在提交写入之前记录；仅首个绑定记录一次
        // （重选成功沿用原休息位），失败在下文恢复。
        if (!_hasPrePerchPosition)
        {
            _prePerchLeft = _owner.Left;
            _prePerchTop = _owner.Top;
        }

        _tracker.Reset();
        var selected = _tracker.Select(snapshot, selfProcessId);
        if (selected && TryApplyLanding("landed") == PerchLandingResult.Landed)
        {
            _hasPrePerchPosition = true;
            _updateGate.Reset(snapshot.BoundsLeft, snapshot.BoundsTop,
                snapshot.BoundsRight, snapshot.BoundsBottom);
            IsActive = true;
            _timer.Start();
            return true;
        }

        // 初选/重选失败：恢复旧绑定（无旧绑定=首次初选，NoTarget 即一致）。
        _tracker.Reset();
        _hasPrePerchPosition = oldHasPrePerch;
        _prePerchLeft = oldPreLeft;
        _prePerchTop = oldPreTop;
        if (oldTarget is not null && _tracker.Select(oldTarget, selfProcessId))
            return false; // 旧绑定原样保留，轮询继续跟随原目标

        // 旧绑定恢复失败（理论不可达）：完整脱离，不留激活残态。
        if (IsActive)
        {
            _timer.Stop();
            IsActive = false;
        }
        return false;
    }

    /// <summary>
    /// P5A-001 R1 F-03：退出保存前恢复栖息前位置（停轮询、解绑、移回
    /// 栖息前窗口位置），保证存档坐标是休息位而非临时栖息位。
    /// 仅在栖息活跃时生效；直接写 Left/Top（退出路径，守卫不适用）。
    /// </summary>
    public void RestorePrePerchPositionForExit()
    {
        if (!IsActive)
            return;
        _owner.EndPerchWobble();
        _timer.Stop();
        IsActive = false;
        _tracker.Reset();
        if (_hasPrePerchPosition)
        {
            _owner.Left = _prePerchLeft;
            _owner.Top = _prePerchTop;
        }
        _hasPrePerchPosition = false;
    }

    /// <summary>
    /// 用户开始拖动：立即交回位置控制。解除绑定、停止跟随，
    /// 不恢复宠物位置（拖动锚点按当前位置计算）。
    /// 记录被取消句柄，阻止松手后自动重选拉回旧目标。
    /// </summary>
    public void CancelForDrag()
    {
        if (!IsActive)
            return;
        _dragCancelledHwndValue = _tracker.Target?.HwndValue;
        Emit("drag-preempted", targetHwnd: _dragCancelledHwndValue);
        DetachInternal(null);
    }

    /// <summary>暂停：脱离且不改变位置；恢复后不续接旧目标（不自动重选）。</summary>
    public void DetachForPause()
    {
        if (!IsActive)
            return;
        Emit("paused-detach", targetHwnd: _tracker.Target?.HwndValue);
        DetachInternal(null);
    }

    /// <summary>立即脱离（失效/遮挡/退出）。宠物留在当前位置。</summary>
    public void Detach(string reason)
    {
        if (!IsActive)
            return;
        DetachInternal(reason);
    }

    private void OnTick()
    {
        if (!IsActive)
            return;

        if (_owner.IsAppPausedNow)
        {
            DetachForPause();
            return;
        }

        // 拖动/抚摸期间不写位置（StartDragging 已即时抢占；这里兜底）。
        if (_owner.IsDraggingNow)
            return;

        // F-02：位置被更高优先级行为占用（物理/探头/社交等）时本轮
        // 不写、不报、不消费跟随基线；保持绑定待其让出。
        if (!_owner.IsPerchWriteAllowed)
            return;

        var selfPid = (uint)Environment.ProcessId;
        var target = _tracker.Target!;
        // R3 F-04 测试缝：仅替换快照采集，其余生产轮询路径不变。
        var snap = TestSnapshotSource is not null
            ? TestSnapshotSource(target.HwndValue)
            : WindowPerchInterop.SnapshotWindow(
                WindowPerchInterop.HwndFromValue(target.HwndValue), ResolveProcessName);
        // 句柄已销毁：构造 dead 快照，由状态机判 WindowGone。
        var observed = snap ?? DeadSnapshot(target.HwndValue);
        var outcome = _tracker.Observe(observed, selfPid);

        if (outcome == PerchObservationOutcome.Cleared)
        {
            DetachInternal(_tracker.ClearReason.ToString());
            return;
        }
        if (outcome != PerchObservationOutcome.Updated)
            return;

        var latest = _tracker.LastObserved!;
        if (_updateGate.PeekShouldReport(
                latest.BoundsLeft, latest.BoundsTop, latest.BoundsRight, latest.BoundsBottom))
        {
            // 目标外框变化（移动/最大化/还原）：跟随到新上缘。
            // F-01：完整宠物矩形在可见区域内容不下 → no-landing 脱离；
            // F-04：实际落脚区被遮挡 → occluded-detach（提交前拒绝，
            // 不会先写入被遮挡/出屏位置）。
            var result = TryApplyLanding("follow");
            switch (result)
            {
                case PerchLandingResult.Landed:
                    // 提交成功才消费基线（F-02：无法提交不消费）。
                    _updateGate.ShouldReport(
                        latest.BoundsLeft, latest.BoundsTop, latest.BoundsRight, latest.BoundsBottom);
                    // P5A-002：跟随成功——短暂晃动（纯视觉，脚底贴住
                    // 目标上沿；不改位置，不改落位/脱离逻辑）。
                    _owner.NotifyPerchFollowLanded();
                    break;
                case PerchLandingResult.NoLanding:
                    DetachInternal("no-landing");
                    return;
                case PerchLandingResult.Occluded:
                    Emit("occluded-detach", targetHwnd: latest.HwndValue, targetBounds: Bounds(latest));
                    DetachInternal(null);
                    return;
                case PerchLandingResult.WriteRejected:
                    break; // 不报不消费，下轮重试
            }
            return;
        }

        // 稳态：目标未动，检查当前落脚区是否被新窗口盖住（R1 F-04：
        // 遮挡判定与实际落脚点一致——只盖落脚区之外的远端不脱离）。
        // P5A-003 R1 F-02：宠物自己的右键菜单打开期间挂起遮挡判定——
        // 菜单（TrackPopupMenuEx 弹层）是自家 UI，会短暂盖住落脚区，
        // 若按外来窗口处理，栖息中右键想选「Leave Perch」会被误判遮挡
        // 而先行脱离（M-16 实机实测 fraction=0.44 先触发）。菜单关闭后
        // 下一轮轮询自动恢复判定。
        if (_owner.IsContextMenuOpen)
            return;
        var (fraction, occluders) = WindowPerchInterop.SampleLandingOcclusion(
            latest, _lastFootX, _lastPetWidthPx, OcclusionSampleCount, _ => "");
        if (WindowPerchOcclusionPolicy.ShouldDetach(fraction))
        {
            Emit("occluded-detach",
                fraction: fraction,
                targetHwnd: latest.HwndValue,
                targetBounds: Bounds(latest),
                reason: string.Join("|", occluders));
            DetachInternal(null);
        }
    }

    /// <summary>
    /// 把宠物落位/跟随到当前目标的可见外框上缘中点。
    /// R1 F-01：以完整宠物矩形校验可见区域（脚点所在显示器边界），
    /// 容不下返回 NoLanding。
    /// R1 F-04：提交前校验实际落脚区遮挡（采样区=宠物在目标上缘的
    /// 实际落脚区 [footX±petWidthPx/2]），被盖返回 Occluded；
    /// 校验通过才提交（先校验后写入）。
    /// R1 F-02：守卫拒绝（更高优先级行为占用位置）返回 WriteRejected，
    /// 不报 landed/follow、不移动。
    /// </summary>
    private PerchLandingResult TryApplyLanding(string eventType)
    {
        var latest = _tracker.LastObserved;
        if (!WindowPerchLandingPolicy.TryComputeLanding(latest, out var footX, out var footY))
            return PerchLandingResult.NoLanding;

        var (petWidthPx, petHeightPx) = _owner.GetPetPhysicalSizePx();
        if (!WindowPerchInterop.TryGetMonitorBoundsForPoint(
                (int)Math.Round(footX), (int)Math.Round(footY),
                out var monLeft, out var monTop, out var monRight, out var monBottom))
            return PerchLandingResult.NoLanding;
        if (!WindowPerchLandingPolicy.TryComputeLanding(
                latest, petWidthPx, petHeightPx,
                monLeft, monTop, monRight, monBottom,
                out footX, out footY))
            return PerchLandingResult.NoLanding;

        var (fraction, occluders) = TestLandingOcclusionSampler is not null
            ? TestLandingOcclusionSampler(latest!, footX, petWidthPx)
            : WindowPerchInterop.SampleLandingOcclusion(
                latest!, footX, petWidthPx, OcclusionSampleCount, _ => "");
        if (WindowPerchOcclusionPolicy.ShouldDetach(fraction))
            return PerchLandingResult.Occluded;

        if (!_owner.ApplyPerchFoot(new PetWorldPosition(footX, footY)))
            return PerchLandingResult.WriteRejected;

        _lastFootX = footX;
        _lastPetWidthPx = petWidthPx;
        Emit(eventType, targetHwnd: latest!.HwndValue, targetBounds: Bounds(latest), footX: footX, footY: footY);
        return PerchLandingResult.Landed;
    }

    private void DetachInternal(string? reason)
    {
        // P5A-002：脱离（拖动/暂停/遮挡/失效/关闭/退出）立即结束晃动，
        // 打断后画面无残留。
        _owner.EndPerchWobble();
        _timer.Stop();
        IsActive = false;
        _tracker.Reset();
        _hasPrePerchPosition = false;
        if (reason is not null)
            Emit("detached", reason: reason);
    }

    private void Emit(string type, string? reason = null, double? fraction = null,
        long? targetHwnd = null, int[]? targetBounds = null, double footX = 0, double footY = 0)
        => PerchEvent?.Invoke(new PetPerchEvent(type, reason, fraction, targetHwnd, targetBounds, footX, footY));

    private static int[] Bounds(ExternalWindowSnapshot s)
        => new[] { s.BoundsLeft, s.BoundsTop, s.BoundsRight, s.BoundsBottom };

    private static ExternalWindowSnapshot DeadSnapshot(long hwndValue) => new(
        hwndValue, "", "", 0, "", false, false, false, false, false, 0, 0, 0, 0);

    private string ResolveProcessName(uint pid)
    {
        if (_pidNameCache.TryGetValue(pid, out var cached)) return cached;
        string name;
        try { name = Process.GetProcessById((int)pid).ProcessName; }
        catch { name = ""; }
        _pidNameCache[pid] = name;
        return name;
    }
}
