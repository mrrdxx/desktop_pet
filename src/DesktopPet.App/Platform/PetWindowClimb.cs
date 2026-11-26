using System;
using System.Collections.Generic;
using System.Diagnostics;
using DesktopPet.Core.Geometry;
using DesktopPet.Core.Windows;

namespace DesktopPet.App.Platform;

/// <summary>攀爬事件（探针/测试观测用）。</summary>
public sealed record PetClimbEvent(
    string Type,
    string? Reason = null,
    double? Fraction = null,
    long? TargetHwnd = null,
    int[]? TargetBounds = null,
    double FootX = 0,
    double FootY = 0);

/// <summary>一次起攀尝试的结果。</summary>
public enum PetClimbStartResult
{
    Started,
    /// <summary>位置所有权被更高优先级行为占用（拖动/抚摸/暂停/物理/探头/社交）。</summary>
    RejectedBusy,
    /// <summary>宠物脚点距目标可见外框超过起攀距离阈值。</summary>
    RejectedTooFar,
    /// <summary>顶部落位脚点不可用（容不下可见区域/监视器查询失败）。</summary>
    RejectedNoLanding,
    /// <summary>顶部落位脚点被遮挡。</summary>
    RejectedOccluded,
    /// <summary>目标不合格（最小化/隐藏/失效/壳窗口等）。</summary>
    RejectedIneligible,
    /// <summary>已在攀爬中（不重复起攀）。</summary>
    RejectedAlreadyActive,
}

/// <summary>
/// P5B-001（R-08）：单宠窗口有限攀爬控制器（隔离预览首版）。
/// 表演式路线：靠近目标可见外框侧边 → 沿侧边爬升到外框上缘 → 沿上缘
/// 到顶部落位脚点（<see cref="WindowPerchLandingPolicy"/> 上缘中点），
/// 落位后交接 <see cref="PetWindowPerch.TrySelectExplicit"/> 进入既有
/// 栖息跟随/遮挡/脱离语义（落位点一致，无可见跳动）。
/// 纪律（ARCHITECTURE §5/§9）：
/// - 每 tick 重观测目标（<see cref="PerchTargetTracker"/>）：目标移动/
///   最小化/关闭/失去资格时路线按最新快照重建或取消；
/// - 路线失败（无落点/落点被遮挡/目标失效/守卫拒绝持续）时取消并回到
///   可见区域（钳回工作区）；
/// - 用户拖动立即接管（<see cref="CancelForDrag"/>，不恢复位置）；
///   暂停/退出结束路线（<see cref="CancelForPause"/>/<see cref="CancelForExit"/>）；
/// - 攀爬期间持有位置所有权（MainWindow.HasCorePositionConflict），
///   栖息初选/跟随、社交位移不得并发写入；
/// - 攀爬动作为临时表现（复用现有动作），不做 Win32 调用（采集走
///   <see cref="WindowPerchInterop"/>）；坐标为虚拟桌面物理像素。
/// 仅在 UI 线程使用。
/// </summary>
public sealed class PetWindowClimb
{
    private const int OcclusionSampleCount = 9;

    /// <summary>R7 F-05：脚点投影距侧边轴在此范围内视为"贴边"，不切换镜像。</summary>
    private const double SideWallEpsilonPx = 0.5;

    private readonly MainWindow _owner;
    private readonly PerchTargetTracker _tracker = new();
    private readonly Dictionary<uint, string> _pidNameCache = new();

    private double _lastTickMs;
    private double _initialStraightDistancePx;
    private bool _hasPreClimbPosition;
    private double _preClimbLeft;
    private double _preClimbTop;

    // P5B-003 S2：当前攀爬动作与镜像方向（避免每 tick 变化时重播动画）。
    // R7 F-04：缓存只记录"实际成功播放"的组合（PlayClimbAction 返回
    // true）；被守卫拒绝或动作缺失时不更新，表达释放后下一 tick 经
    // DispatchStageAction 自动补播。
    private string? _lastClimbActionId;
    private bool _lastClimbMirrorLeft;

    /// <summary>当前是否正在攀爬路线中。</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// R7 F-03：是否存在攀爬呈现活动——路线进行中，或路线已结束但
    /// climb_* 动作仍在播放（到达交接栖息后 land once 尚未播完）。
    /// 生产中断（拖动/暂停/找回/退出）即使路线已结束也必须清理残留
    /// 呈现，否则 land 残影连同镜像悬挂在宠物体上。
    /// </summary>
    private bool IsPresentingClimb
        => IsActive || IsClimbActionId(_owner.CurrentAction);

    private static bool IsClimbActionId(string actionId)
        => actionId == "climb_side" || actionId == "climb_edge" || actionId == "climb_land";

    /// <summary>当前目标句柄数值（仅 IsActive 时非空）。</summary>
    public long? TargetHwndValue => IsActive ? _tracker.Target?.HwndValue : null;

    /// <summary>爬行进度 0..1（直线距离口径，探针/测试观测）。</summary>
    public double ProgressFraction { get; private set; }

    /// <summary>爬行事件流（探针写 jsonl；生产可为空）。</summary>
    public event Action<PetClimbEvent>? ClimbEvent;

    /// <summary>测试缝：覆盖目标快照采集（返回 null 等价于句柄已销毁）。仅测试使用。</summary>
    internal Func<long, ExternalWindowSnapshot?>? TestSnapshotSource;

    /// <summary>测试缝：覆盖落位遮挡采样（返回 (可见比例, 遮挡者列表)）。仅测试使用。</summary>
    internal Func<ExternalWindowSnapshot, double, double, (double VisibleFraction, List<string> Occluders)>?
        TestLandingOcclusionSampler;

    /// <summary>测试缝：覆盖爬行速度（物理像素/毫秒）。null=生产默认。</summary>
    internal double? TestSpeedPxPerMs;

    /// <summary>测试缝：覆盖起攀距离阈值（物理像素）。null=生产默认。</summary>
    internal double? TestMaxStartDistancePx;

    public PetWindowClimb(MainWindow owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// 显式起攀：校验目标资格、起攀距离与顶部落点（可见区域 + 落脚区
    /// 遮挡），全部通过后沿侧边→上缘路线爬向顶部落位脚点。
    /// 任何校验失败不改变状态、不移动。
    /// </summary>
    public PetClimbStartResult TryStart(ExternalWindowSnapshot snapshot, uint selfProcessId, double nowMs)
    {
        if (IsActive)
            return PetClimbStartResult.RejectedAlreadyActive;
        if (!_owner.IsClimbWriteAllowed)
            return PetClimbStartResult.RejectedBusy;
        if (ExternalWindowEligibility.Evaluate(snapshot, selfProcessId) != ExternalWindowExclusion.None)
            return PetClimbStartResult.RejectedIneligible;

        var foot = CurrentFootPx();
        var maxDistance = TestMaxStartDistancePx ?? WindowClimbPolicy.DefaultMaxStartDistancePx;
        if (!WindowClimbPolicy.IsStartDistanceSatisfied(snapshot, foot.FootCenterX, foot.FootCenterY, maxDistance))
            return PetClimbStartResult.RejectedTooFar;

        if (!TryComputeLandingForTarget(snapshot, out var landingX, out var landingY))
            return PetClimbStartResult.RejectedNoLanding;
        if (IsLandingOccluded(snapshot, landingX, out var fraction))
            return PetClimbStartResult.RejectedOccluded;

        // 起攀成功：记录爬行前位置（DIP），路线中位置不写存档
        // （取消/失败回到可见区域；退出路径恢复爬行前位置）。
        _hasPreClimbPosition = true;
        _preClimbLeft = _owner.Left;
        _preClimbTop = _owner.Top;

        _tracker.Reset();
        if (!_tracker.Select(snapshot, selfProcessId))
            return PetClimbStartResult.RejectedIneligible;

        IsActive = true;
        _lastTickMs = nowMs;
        _initialStraightDistancePx = Math.Sqrt(
            (landingX - foot.FootCenterX) * (landingX - foot.FootCenterX)
            + (landingY - foot.FootCenterY) * (landingY - foot.FootCenterY));
        ProgressFraction = 0;
        // P5B-003 S2：起攀即播侧边爬升动作；素材默认朝左伸爪，
        // 目标在宠物右侧（最近侧边在脚点右侧）时镜像翻转。
        // R7 F-04：只在实际成功播放后记录缓存——临时表达占用（好奇等）
        // 时守卫拒绝播放但路线照常开始，表达释放后下一 tick 经
        // DispatchStageAction 自动补播 climb_side。
        var sideX = Math.Abs(foot.FootCenterX - snapshot.BoundsLeft)
            <= Math.Abs(foot.FootCenterX - snapshot.BoundsRight)
            ? snapshot.BoundsLeft
            : snapshot.BoundsRight;
        var mirrorLeft = sideX > foot.FootCenterX;
        if (_owner.PlayClimbAction("climb_side", mirrorLeft))
        {
            _lastClimbActionId = "climb_side";
            _lastClimbMirrorLeft = mirrorLeft;
        }
        Emit("climb-started", targetHwnd: snapshot.HwndValue, targetBounds: Bounds(snapshot),
            footX: foot.FootCenterX, footY: foot.FootCenterY);
        return PetClimbStartResult.Started;
    }

    /// <summary>
    /// 路线推进（OnUpdateTick 每帧调用）。每 tick 重观测目标：
    /// - 目标移动：按最新外框重建剩余路线（固定速度，不重置进度）；
    /// - 目标失效/无落点/落点被遮挡：取消并回到可见区域；
    /// - 到达落位脚点：结束路线并交接栖息（TrySelectExplicit 落位点一致）。
    /// </summary>
    public void Tick(double nowMs)
    {
        if (!IsActive)
            return;

        if (_owner.IsAppPausedNow || _owner.IsDraggingNow)
            return; // 暂停/拖动同步入口已取消；此处兜底不推进。

        if (!_owner.IsClimbWriteAllowed)
        {
            // 位置被更高优先级行为（物理/探头/社交等）占用：结束路线让出位置。
            CancelCore("preempted");
            return;
        }

        var dtMs = nowMs - _lastTickMs;
        // F-02：单调时间域——时钟未前进（含回退）时不推进、不发步事件，
        // 不重复消费积压；生产计时器持续前进，正常路径不受影响。
        if (dtMs <= 0)
            return;
        _lastTickMs = nowMs;

        var selfPid = (uint)Environment.ProcessId;
        var target = _tracker.Target!;
        var snap = TestSnapshotSource is not null
            ? TestSnapshotSource(target.HwndValue)
            : WindowPerchInterop.SnapshotWindow(
                WindowPerchInterop.HwndFromValue(target.HwndValue), ResolveProcessName);
        var observed = snap ?? DeadSnapshot(target.HwndValue);
        if (_tracker.Observe(observed, selfPid) == PerchObservationOutcome.Cleared)
        {
            CancelCore("target-" + _tracker.ClearReason);
            return;
        }

        var latest = _tracker.LastObserved!;
        if (!TryComputeLandingForTarget(latest, out var landingX, out var landingY))
        {
            CancelCore("no-landing");
            return;
        }
        if (IsLandingOccluded(latest, landingX, out var occFraction))
        {
            Emit("climb-occluded", fraction: occFraction, targetHwnd: latest.HwndValue,
                targetBounds: Bounds(latest));
            CancelCore("occluded");
            return;
        }

        var foot = CurrentFootPx();
        var speed = TestSpeedPxPerMs ?? WindowClimbPolicy.DefaultSpeedPxPerMs;
        // F-02：有限补步——单 tick 最多推进 MaxStepTimeMs 对应的距离；
        // 更长停顿产生的积压直接丢弃（_lastTickMs 已更新，后续 tick
        // 不会补计，恢复后不跳变到落点）。
        var stepMs = Math.Min(dtMs, WindowClimbPolicy.MaxStepTimeMs);
        var advancePx = speed * stepMs;

        // 剩余路线：当前脚点 → 最近侧边 → 上缘 → 落位脚点。
        var (w1x, w1y, w2x, w2y) = WindowClimbPolicy.BuildRoute(
            latest, foot.FootCenterX, foot.FootCenterY, landingX, landingY);
        // F-02：到达判定基于真实剩余折线长度，而非终点欧氏距离——
        // 直线距离可能短于折线（绕侧边上缘的长度更长），用欧氏判定会
        // 提前交接造成落位跳跃。
        var remainingPolylinePx = WindowClimbPolicy.PolylineLengthPx(
            foot.FootCenterX, foot.FootCenterY, w1x, w1y, w2x, w2y, landingX, landingY);

        if (remainingPolylinePx <= advancePx || remainingPolylinePx <= 1.0)
        {
            // 先由攀爬持有者提交最终脚点，再释放位置交给栖息。
            if (!_owner.ApplyPerchFoot(new PetWorldPosition(landingX, landingY), fromClimb: true))
            {
                CancelCore("write-rejected");
                return;
            }
            Emit("climb-arrived", targetHwnd: latest.HwndValue,
                targetBounds: Bounds(latest), footX: landingX, footY: landingY);
            // P5B-003 S2：到达播落位动作（once）。播放成功才保留 land
            // 呈现并 StopCore(keepLand: true) 跳过立即回 idle，让 land
            // 自然播完（栖息结构化只移位置不切动作，不打断 land）；播完
            // 后 OnActionCompleted 回 idle 并复位镜像。
            // R7 F-04：播放失败（缺失/被守卫）时立即走 keepLand:false
            // 清理——回 idle、复位镜像，不残留攀爬动作。
            if (_owner.PlayClimbAction("climb_land", _lastClimbMirrorLeft))
            {
                _lastClimbActionId = "climb_land";
                StopCore(keepLand: true);
            }
            else
            {
                StopCore(keepLand: false);
            }
            var selected = _owner.Perch.TrySelectExplicit(latest, selfPid);
            var finalFoot = CurrentFootPx();
            Emit(selected ? "climb-completed" : "climb-completed-no-handoff",
                targetHwnd: latest.HwndValue, targetBounds: Bounds(latest),
                fraction: 1.0, footX: finalFoot.FootCenterX, footY: finalFoot.FootCenterY);
            if (!selected)
                ReturnToVisibleArea(); // 交接失败（理论上仅时序竞争）：回到可见区域。
            return;
        }

        // P5B-003 S2：非到达——按路线阶段分派侧边/上缘动作（仅变化时重播）。
        // 上缘段判据与 WindowClimbPolicy.BuildRoute 同源（脚点已到上缘高度
        // 且水平在外框跨度内）；不能靠 BuildRoute 返回值特征推断——脚点恰在
        // 侧边 x 轴上时非上缘路径的 w1 也会是 (footX, footY)。
        var onTopEdge = foot.FootCenterY <= latest.BoundsTop
            && foot.FootCenterX >= latest.BoundsLeft
            && foot.FootCenterX <= latest.BoundsRight;
        DispatchStageAction(
            w1x, w1y, w2x, w2y, foot.FootCenterX, foot.FootCenterY, landingX, onTopEdge);

        var (nextX, nextY) = WindowClimbPolicy.AdvanceAlongRoute(
            foot.FootCenterX, foot.FootCenterY, w1x, w1y, w2x, w2y, landingX, landingY, advancePx);

        if (!_owner.ApplyPerchFoot(new PetWorldPosition(nextX, nextY), fromClimb: true))
        {
            // 写入被守卫拒绝（竞态让位）：结束路线让出位置。
            CancelCore("write-rejected");
            return;
        }

        var remainingAfter = Math.Sqrt(
            (landingX - nextX) * (landingX - nextX)
            + (landingY - nextY) * (landingY - nextY));
        if (_initialStraightDistancePx > 0)
            ProgressFraction = Math.Min(1.0, 1.0 - remainingAfter / _initialStraightDistancePx);
        Emit("climb-step", fraction: ProgressFraction, targetHwnd: latest.HwndValue,
            targetBounds: Bounds(latest), footX: nextX, footY: nextY);
    }

    /// <summary>用户开始拖动：立即交回位置控制。结束路线、不恢复位置。
    /// R7 F-03：land 仍在播（路线已结束）时同样清理呈现。</summary>
    public void CancelForDrag()
    {
        if (!IsPresentingClimb)
            return;
        var hwnd = _tracker.Target?.HwndValue;
        StopCore();
        Emit("climb-drag-preempted", targetHwnd: hwnd);
    }

    /// <summary>暂停：结束路线（恢复后不续接）。R7 F-03：land 仍在播时同样清理。</summary>
    public void CancelForPause()
    {
        if (!IsPresentingClimb)
            return;
        CancelCore("paused");
    }

    /// <summary>
    /// P5B-001 R1 F-01：找回（RecoverPet）同步取消路线——找回完成后
    /// 路线不得仍活跃，否则后续 Tick 会把宠物拉回原目标。
    /// R7 F-03：land 仍在播（路线已结束）时同样清理呈现。
    /// </summary>
    public void CancelForRecover()
    {
        if (!IsPresentingClimb)
            return;
        var hwnd = _tracker.Target?.HwndValue;
        StopCore();
        Emit("climb-cancelled", reason: "recovered", targetHwnd: hwnd);
    }

    /// <summary>
    /// R8 F-03：栖息实际脱离（目标失效/最小化/遮挡，或主动脱离/拖动/
    /// 暂停/找回——生产 Perch.OnTick 轮询 Cleared/no-landing/occluded
    /// 及 Detach/DetachForPause/CancelForDrag 均收敛到
    /// <see cref="PetWindowPerch"/> 的 DetachInternal）时，释放仍由攀爬
    /// 持有的落位表现：到达交接后 climb_land 可能在播（路线已结束），
    /// 目标一旦失效必须同步清理动作与镜像，不得让 land 残影悬挂。
    /// 与 <see cref="CancelForDrag"/> 同语义：不移动、不恢复位置
    /// （栖息脱离语义=宠物留在当前位置）；不与 Pause/Recover/Exit 的
    /// 既有清理重复（IsPresentingClimb 守卫使其在已清理后变 no-op）。
    /// </summary>
    public void CancelForPerchDetached()
    {
        if (!IsPresentingClimb)
            return;
        var hwnd = _tracker.Target?.HwndValue;
        StopCore();
        Emit("climb-cancelled", reason: "perch-detached", targetHwnd: hwnd);
    }

    /// <summary>退出：结束路线并把窗口移回爬行前位置（保证存档是休息位）。
    /// R7 F-03：land 仍在播（路线已结束）时同样清理呈现；到达后爬行前
    /// 位置已释放，不恢复（落位点即合法休息位）。</summary>
    public void CancelForExit()
    {
        if (!IsPresentingClimb)
            return;
        // 先取回爬行前位置再停路线（StopCore 清恢复标记）。
        var restoreLeft = _preClimbLeft;
        var restoreTop = _preClimbTop;
        var hasRestore = _hasPreClimbPosition;
        StopCore();
        if (hasRestore)
        {
            _owner.Left = restoreLeft;
            _owner.Top = restoreTop;
        }
    }

    /// <summary>
    /// 取消并回到可见区域：路线失败（目标失效/无落点/遮挡/被抢占）时
    /// 结束路线，把宠物钳回当前显示器可见工作区（宠物可能停在目标
    /// 侧边/上缘的临时位置）。
    /// </summary>
    private void CancelCore(string reason)
    {
        var hwnd = _tracker.Target?.HwndValue;
        StopCore();
        ReturnToVisibleArea();
        Emit("climb-cancelled", reason: reason, targetHwnd: hwnd);
    }

    /// <summary>结束路线：清所有权旗、恢复爬行前表现、丢弃目标。不移动。
    /// P5B-003 S2：<paramref name="keepLand"/>=true（到达分支已播 climb_land
    /// 并交栖息）时跳过立即回 idle，让 land once 自然播完。</summary>
    private void StopCore(bool keepLand = false)
    {
        IsActive = false;
        ProgressFraction = 0;
        _hasPreClimbPosition = false;
        _tracker.Reset();
        _owner.EndClimbAction(keepLand);
    }

    /// <summary>
    /// P5B-003 S2：按路线阶段分派攀爬动作。上缘段（<paramref name="onTopEdge"/>，
    /// 与 <see cref="WindowClimbPolicy.BuildRoute"/> 同源判定）→ edge，方向用
    /// 落位脚点与当前脚点比较；否则 → side，方向用最近侧边（w1x）与脚点比较。
    /// R7 F-04：仅在实际成功播放后才更新缓存；播放被守卫拒绝/动作缺失时
    /// 保持上一缓存，下一 tick 重试（临时表达释放后自动补播）。
    /// R7 F-05：脚点已贴到侧边轴（数值相等误差内）时保持上一镜像，不翻转
    /// ——沿墙直爬阶段镜像不得在轴两侧抖动。</summary>
    private void DispatchStageAction(
        double w1x, double w1y, double w2x, double w2y,
        double footX, double footY, double landingX, bool onTopEdge)
    {
        string actionId;
        bool mirrorLeft;
        if (onTopEdge)
        {
            actionId = "climb_edge";
            mirrorLeft = landingX > footX;
        }
        else
        {
            actionId = "climb_side";
            mirrorLeft = Math.Abs(w1x - footX) <= SideWallEpsilonPx
                ? _lastClimbMirrorLeft
                : w1x > footX;
        }
        if (_lastClimbActionId == actionId && _lastClimbMirrorLeft == mirrorLeft)
        {
            // R8 F-04：缓存相等只说明"上一 tick 实际成功播过该组合"。
            // 阶段已成功播过、随后被更高优先级表达（curious 等）覆盖、
            // 表达释放回 idle 后，缓存相等但真实播放已不是请求阶段——
            // 必须核对当前实际播放动作与镜像；一致才可跳过（正常同阶段
            // 不重启），不一致则继续重试 PlayClimbAction 补播。
            if (_owner.CurrentAction == actionId
                && (_owner.ClimbMirrorTransform.ScaleX < 0) == mirrorLeft)
                return;
        }
        if (!_owner.PlayClimbAction(actionId, mirrorLeft))
            return;
        _lastClimbActionId = actionId;
        _lastClimbMirrorLeft = mirrorLeft;
    }

    private void ReturnToVisibleArea() => _owner.ClampWindowToVisibleWorkArea();

    /// <summary>
    /// 顶部落位脚点（含完整宠物矩形的可见区域校验与落脚区遮挡由调用方
    /// 分别判定）：与 <see cref="PetWindowPerch"/> 同一落位策略。
    /// </summary>
    private bool TryComputeLandingForTarget(
        ExternalWindowSnapshot target, out double footX, out double footY)
    {
        footX = footY = 0;
        var (petWidthPx, petHeightPx) = _owner.GetPetPhysicalSizePx();
        if (!WindowPerchInterop.TryGetMonitorBoundsForPoint(
                (int)Math.Round((target.BoundsLeft + target.BoundsRight) / 2.0),
                target.BoundsTop,
                out var monLeft, out var monTop, out var monRight, out var monBottom))
            return false;
        return WindowPerchLandingPolicy.TryComputeLanding(
            target, petWidthPx, petHeightPx, monLeft, monTop, monRight, monBottom,
            out footX, out footY);
    }

    private bool IsLandingOccluded(ExternalWindowSnapshot target, double footX, out double fraction)
    {
        var petWidthPx = _owner.GetPetPhysicalSizePx().Item1;
        var (visibleFraction, _) = TestLandingOcclusionSampler is not null
            ? TestLandingOcclusionSampler(target, footX, petWidthPx)
            : WindowPerchInterop.SampleLandingOcclusion(
                target, footX, petWidthPx, OcclusionSampleCount, _ => "");
        fraction = visibleFraction;
        return WindowPerchOcclusionPolicy.ShouldDetach(visibleFraction);
    }

    private PetWorldPosition CurrentFootPx()
    {
        var (dpiX, dpiY) = MonitorInterop.GetWindowDpiScale(
            new System.Windows.Interop.WindowInteropHelper(_owner).Handle);
        return CoordinateTransform.WindowTopLeftToFootCenter(
            _owner.Left, _owner.Top, _owner.Width, _owner.Height, dpiX, dpiY);
    }

    private void Emit(string type, string? reason = null, double? fraction = null,
        long? targetHwnd = null, int[]? targetBounds = null, double footX = 0, double footY = 0)
        => ClimbEvent?.Invoke(new PetClimbEvent(type, reason, fraction, targetHwnd, targetBounds, footX, footY));

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
