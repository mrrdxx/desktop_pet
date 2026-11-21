using DesktopPet.Core.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>P5A-PLAN：外部窗口资格筛选 / 身份验证 / 栖息目标状态机单元测试。</summary>
[TestClass]
public class ExternalWindowTests
{
    private const uint SelfPid = 400;
    private const uint TargetPid = 500;

    private static ExternalWindowSnapshot Snapshot(
        long hwnd = 0x10101,
        string title = "无标题 - 记事本",
        string className = "Notepad",
        uint pid = TargetPid,
        string processName = "notepad",
        bool alive = true,
        bool visible = true,
        bool minimized = false,
        bool cloaked = false,
        bool toolWindow = false,
        int left = 100, int top = 100, int right = 500, int bottom = 400) => new(
            hwnd, title, className, pid, processName,
            alive, visible, minimized, cloaked, toolWindow,
            left, top, right, bottom);

    // ---------- ExternalWindowEligibility ----------

    [TestMethod]
    public void Eligibility_OrdinaryWindow_IsEligible()
    {
        Assert.AreEqual(ExternalWindowExclusion.None,
            ExternalWindowEligibility.Evaluate(Snapshot(), SelfPid));
    }

    [TestMethod]
    public void Eligibility_NegativeCoordinateWindow_IsEligible()
    {
        // 虚拟桌面物理像素坐标允许负值（左侧扩展屏）。
        Assert.AreEqual(ExternalWindowExclusion.None,
            ExternalWindowEligibility.Evaluate(Snapshot(left: -1920, top: -200, right: -100, bottom: 500), SelfPid));
    }

    [TestMethod]
    public void Eligibility_DeadWindow_Excluded()
    {
        Assert.AreEqual(ExternalWindowExclusion.DeadWindow,
            ExternalWindowEligibility.Evaluate(Snapshot(alive: false), SelfPid));
    }

    [TestMethod]
    public void Eligibility_SelfProcess_ExcludedAndTakesPrecedenceOverOthers()
    {
        // 本程序进程优先级仅次于死窗口：不可见 + 自身进程 → 判自身进程。
        Assert.AreEqual(ExternalWindowExclusion.SelfProcess,
            ExternalWindowEligibility.Evaluate(Snapshot(pid: SelfPid, visible: false), SelfPid));
    }

    [TestMethod]
    public void Eligibility_KnownShellClasses_Excluded()
    {
        Assert.AreEqual(ExternalWindowExclusion.KnownShellWindow,
            ExternalWindowEligibility.Evaluate(Snapshot(title: "Program Manager", className: "Progman"), SelfPid));
        Assert.AreEqual(ExternalWindowExclusion.KnownShellWindow,
            ExternalWindowEligibility.Evaluate(Snapshot(title: "", className: "Shell_TrayWnd"), SelfPid));
    }

    [TestMethod]
    public void Eligibility_NotVisible_Excluded()
    {
        Assert.AreEqual(ExternalWindowExclusion.NotVisible,
            ExternalWindowEligibility.Evaluate(Snapshot(visible: false), SelfPid));
    }

    [TestMethod]
    public void Eligibility_Minimized_Excluded()
    {
        Assert.AreEqual(ExternalWindowExclusion.Minimized,
            ExternalWindowEligibility.Evaluate(Snapshot(minimized: true), SelfPid));
    }

    [TestMethod]
    public void Eligibility_Cloaked_Excluded()
    {
        Assert.AreEqual(ExternalWindowExclusion.Cloaked,
            ExternalWindowEligibility.Evaluate(Snapshot(cloaked: true), SelfPid));
    }

    [TestMethod]
    public void Eligibility_ToolWindow_Excluded()
    {
        Assert.AreEqual(ExternalWindowExclusion.ToolWindow,
            ExternalWindowEligibility.Evaluate(Snapshot(toolWindow: true), SelfPid));
    }

    [TestMethod]
    public void Eligibility_EmptyTitle_Excluded()
    {
        Assert.AreEqual(ExternalWindowExclusion.EmptyTitle,
            ExternalWindowEligibility.Evaluate(Snapshot(title: "  "), SelfPid));
    }

    [TestMethod]
    public void Eligibility_ZeroSizeBounds_Excluded()
    {
        Assert.AreEqual(ExternalWindowExclusion.ZeroSizeBounds,
            ExternalWindowEligibility.Evaluate(Snapshot(right: 100, bottom: 100), SelfPid));
    }

    // ---------- PerchTargetTracker：选择与更新 ----------

    [TestMethod]
    public void Tracker_SelectRequiresEligibleWindow()
    {
        var tracker = new PerchTargetTracker();
        Assert.IsFalse(tracker.Select(Snapshot(visible: false), SelfPid));
        Assert.AreEqual(PerchTargetStatus.NoTarget, tracker.Status);

        Assert.IsTrue(tracker.Select(Snapshot(), SelfPid));
        Assert.AreEqual(PerchTargetStatus.Tracking, tracker.Status);
    }

    [TestMethod]
    public void Tracker_ObserveMove_UpdatesLastObserved()
    {
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);

        var moved = Snapshot(left: 300, top: 250, right: 700, bottom: 550);
        Assert.AreEqual(PerchObservationOutcome.Updated, tracker.Observe(moved, SelfPid));
        Assert.IsNotNull(tracker.LastObserved);
        Assert.AreEqual(300, tracker.LastObserved!.BoundsLeft);
        Assert.AreEqual(PerchTargetStatus.Tracking, tracker.Status);
    }

    [TestMethod]
    public void Tracker_ObserveOtherHandle_Ignored()
    {
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);

        Assert.AreEqual(PerchObservationOutcome.Ignored, tracker.Observe(Snapshot(hwnd: 0x20202), SelfPid));
        Assert.AreEqual(PerchTargetStatus.Tracking, tracker.Status);
    }

    // ---------- 句柄失效与复用 ----------

    [TestMethod]
    public void Tracker_WindowDestroyed_ClearsWithWindowGone()
    {
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);

        Assert.AreEqual(PerchObservationOutcome.Cleared, tracker.Observe(Snapshot(alive: false), SelfPid));
        Assert.AreEqual(PerchTargetStatus.Cleared, tracker.Status);
        Assert.AreEqual(PerchTargetClearReason.WindowGone, tracker.ClearReason);
        Assert.IsNull(tracker.LastObserved);
    }

    [TestMethod]
    public void Tracker_SameHandleDifferentIdentity_ClearsWithHandleReused()
    {
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);

        // 同一句柄数值被其他进程的窗口复用。
        var reused = Snapshot(pid: 777, processName: "otherapp", title: "其他窗口 - 其他程序");
        Assert.AreEqual(PerchObservationOutcome.Cleared, tracker.Observe(reused, SelfPid));
        Assert.AreEqual(PerchTargetClearReason.HandleReused, tracker.ClearReason);
    }

    [TestMethod]
    public void Tracker_ReacquireSameHandleAfterReuse_RejectsDifferentIdentity()
    {
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);
        tracker.Observe(Snapshot(alive: false), SelfPid); // 清除（窗口销毁）

        // 同数值句柄已分配给别的窗口：不得当作原目标恢复。
        var reused = Snapshot(pid: 777, processName: "otherapp", title: "其他窗口 - 其他程序");
        Assert.IsFalse(tracker.TryReacquire(reused, SelfPid));
        Assert.AreEqual(PerchTargetStatus.Cleared, tracker.Status);
    }

    [TestMethod]
    public void Tracker_ReacquireSameHandleAfterWindowGone_RejectsEvenWithSameIdentity()
    {
        // R1 F-02 反例：窗口销毁（WindowGone）后，操作系统可能把同数值句柄分配给
        // 同一程序的新窗口（HWND、PID、类名全部相同）。身份判定无法区分"原窗口还原"
        // 与"新窗口复用"，因此不得自动恢复。
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);
        tracker.Observe(Snapshot(alive: false), SelfPid); // WindowGone

        var lookalike = Snapshot(left: 200, top: 150, right: 600, bottom: 450);
        Assert.IsFalse(tracker.TryReacquire(lookalike, SelfPid));
        Assert.AreEqual(PerchTargetStatus.Cleared, tracker.Status);
        Assert.AreEqual(PerchTargetClearReason.WindowGone, tracker.ClearReason);

        // 必须经 Reset + Select 的明确新目标选择才能重新取得。
        tracker.Reset();
        Assert.AreEqual(PerchTargetStatus.NoTarget, tracker.Status);
        Assert.IsTrue(tracker.Select(lookalike, SelfPid));
        Assert.AreEqual(PerchTargetStatus.Tracking, tracker.Status);
        Assert.AreEqual(200, tracker.LastObserved!.BoundsLeft);
    }

    [TestMethod]
    public void Tracker_ReacquireSameHandleAfterHandleReused_RejectsEvenWithSameIdentity()
    {
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);
        // 同值句柄被其他窗口占用 → HandleReused。
        tracker.Observe(Snapshot(pid: 777, processName: "otherapp", title: "其他窗口 - 其他程序"), SelfPid);
        Assert.AreEqual(PerchTargetClearReason.HandleReused, tracker.ClearReason);

        // 即使随后出现 HWND、PID、类名全部一致的快照也不得自动恢复。
        Assert.IsFalse(tracker.TryReacquire(Snapshot(), SelfPid));
        Assert.AreEqual(PerchTargetStatus.Cleared, tracker.Status);

        tracker.Reset();
        Assert.IsTrue(tracker.Select(Snapshot(), SelfPid));
    }

    [TestMethod]
    public void Tracker_ReacquireOtherHandle_RequiresEligibleWindow()
    {
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);
        tracker.Observe(Snapshot(alive: false), SelfPid);

        // 新句柄：最小化的窗口不得作为新选择恢复。
        Assert.IsFalse(tracker.TryReacquire(Snapshot(hwnd: 0x30303, minimized: true), SelfPid));
        Assert.AreEqual(PerchTargetStatus.Cleared, tracker.Status);

        // 合格的新窗口可作为明确的新选择。
        Assert.IsTrue(tracker.TryReacquire(Snapshot(hwnd: 0x30303, title: "新窗口 - 浏览器", className: "Chrome_WidgetWin_1"), SelfPid));
        Assert.AreEqual(PerchTargetStatus.Tracking, tracker.Status);
    }

    // ---------- PerchUpdateGate：外框更新上报闸门（R1 F-01） ----------

    [TestMethod]
    public void Gate_MovedThenStationary_ReportsOnceOnly()
    {
        // 移动后静止：只上报一次，不重复上报。
        var gate = new PerchUpdateGate();
        gate.Reset(100, 100, 500, 400);

        Assert.IsTrue(gate.ShouldReport(300, 250, 700, 550));   // 移动 → 上报
        Assert.IsFalse(gate.ShouldReport(300, 250, 700, 550));  // 静止 → 不上报
        Assert.IsFalse(gate.ShouldReport(300, 250, 700, 550));  // 仍静止 → 不上报
    }

    [TestMethod]
    public void Gate_MoveBackToInitialPosition_ReportsAgain()
    {
        // 基线是上一次已上报外框（不是初始选择外框）：移回初始位置时必须上报。
        var gate = new PerchUpdateGate();
        gate.Reset(100, 100, 500, 400);

        Assert.IsTrue(gate.ShouldReport(300, 250, 700, 550));
        Assert.IsTrue(gate.ShouldReport(100, 100, 500, 400));   // 移回初始位置 → 上报
        CollectionAssert.AreEqual(new[] { 100, 100, 500, 400 }, gate.LastReported);
    }

    [TestMethod]
    public void Gate_FastMoveSequence_FinalReportedBoundsMatchLastPosition()
    {
        // 快速移动的连续外框都上报，最终基线等于最终外框。
        var gate = new PerchUpdateGate();
        gate.Reset(0, 0, 800, 500);

        int finalL = 0, finalT = 0;
        for (var i = 1; i <= 20; i++)
        {
            finalL = 1500 + i * 10;
            finalT = 830 + i * 5;
            Assert.IsTrue(gate.ShouldReport(finalL, finalT, finalL + 800, finalT + 500));
        }
        CollectionAssert.AreEqual(new[] { finalL, finalT, finalL + 800, finalT + 500 }, gate.LastReported);
    }

    [TestMethod]
    public void Gate_WithoutReset_FirstObservationEstablishesBaseline()
    {
        var gate = new PerchUpdateGate();
        Assert.IsNull(gate.LastReported);
        Assert.IsTrue(gate.ShouldReport(10, 10, 100, 100));     // 无基线：上报并建立
        Assert.IsFalse(gate.ShouldReport(10, 10, 100, 100));
    }

    // ---------- 最小化 / 隐藏 / 失去资格 ----------

    // ---------- WindowPerchSelectionPolicy：R2 F-02 探针轮询选择决策（确定性补证） ----------

    [TestMethod]
    public void Policy_SameHandleAfterWindowGone_NeverRebindsAcrossPersistentPolling()
    {
        // 探针轮询语义的确定性复现：WindowGone 后同值句柄（即使身份字段全部一致）
        // 出现在原筛选结果中，持续轮询 N 次也不得重新绑定（无自动回退）。
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);
        tracker.Observe(Snapshot(alive: false), SelfPid); // WindowGone
        Assert.AreEqual((long)0x10101, tracker.ClearedTargetHwndValue);

        var lookalike = Snapshot(left: 200, top: 150, right: 600, bottom: 450);
        for (var poll = 0; poll < 25; poll++)
        {
            var action = WindowPerchSelectionPolicy.DecideCleared(
                tracker.ClearReason, tracker.ClearedTargetHwndValue == lookalike.HwndValue);
            Assert.AreEqual(PerchPollAction.RejectedSameHandle, action, $"poll #{poll} must not rebind");
            Assert.AreEqual(PerchTargetStatus.Cleared, tracker.Status, $"poll #{poll} status");
            Assert.IsNull(tracker.Target);
        }
    }

    [TestMethod]
    public void Policy_SameHandleAfterHandleReused_NeverRebindsAcrossPersistentPolling()
    {
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);
        tracker.Observe(Snapshot(pid: 777, processName: "otherapp", title: "其他窗口 - 其他程序"), SelfPid);
        Assert.AreEqual(PerchTargetClearReason.HandleReused, tracker.ClearReason);

        for (var poll = 0; poll < 25; poll++)
        {
            var action = WindowPerchSelectionPolicy.DecideCleared(
                tracker.ClearReason, tracker.ClearedTargetHwndValue == Snapshot().HwndValue);
            Assert.AreEqual(PerchPollAction.RejectedSameHandle, action, $"poll #{poll}");
            Assert.AreEqual(PerchTargetStatus.Cleared, tracker.Status);
        }
    }

    [TestMethod]
    public void Policy_SameHandleAfterWindowGone_ExplicitNewSelectionBinds()
    {
        // 显式新选择才成功：同值拒绝后，经独立的 Reset + Select（重开探针指定新目标）
        // 重新绑定成功。
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);
        tracker.Observe(Snapshot(alive: false), SelfPid);

        var lookalike = Snapshot(left: 200, top: 150, right: 600, bottom: 450);
        var action = WindowPerchSelectionPolicy.DecideCleared(
            tracker.ClearReason, tracker.ClearedTargetHwndValue == lookalike.HwndValue);
        Assert.AreEqual(PerchPollAction.RejectedSameHandle, action);

        // 独立的显式新选择输入：丢弃被清除目标记忆后明确选择。
        tracker.Reset();
        Assert.IsTrue(tracker.Select(lookalike, SelfPid));
        Assert.AreEqual(PerchTargetStatus.Tracking, tracker.Status);
        Assert.AreEqual(200, tracker.LastObserved!.BoundsLeft);
    }

    [TestMethod]
    public void Policy_MinimizedRestore_StillAutoReacquires()
    {
        // 最小化还原正例保留：窗口未销毁，同值句柄仍自动重获。
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);
        tracker.Observe(Snapshot(minimized: true), SelfPid);
        Assert.AreEqual(PerchTargetClearReason.Minimized, tracker.ClearReason);

        var action = WindowPerchSelectionPolicy.DecideCleared(
            tracker.ClearReason, tracker.ClearedTargetHwndValue == Snapshot().HwndValue);
        Assert.AreEqual(PerchPollAction.TryReacquire, action);
        Assert.IsTrue(tracker.TryReacquire(Snapshot(), SelfPid));
        Assert.AreEqual(PerchTargetStatus.Tracking, tracker.Status);
    }

    [TestMethod]
    public void Policy_HiddenRestore_AndDifferentHandleCandidate_AutoReacquire()
    {
        // 隐藏还原（窗口未销毁）同值自动恢复；新句柄窗口作为明确新选择自动重获。
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);
        tracker.Observe(Snapshot(visible: false), SelfPid);
        Assert.AreEqual(PerchPollAction.TryReacquire, WindowPerchSelectionPolicy.DecideCleared(
            tracker.ClearReason, candidateMatchesClearedHandle: true));
        Assert.IsTrue(tracker.TryReacquire(Snapshot(), SelfPid));

        // WindowGone 后新句柄窗口（重开的真实场景）：自动重获允许（身份不同）。
        tracker.Observe(Snapshot(alive: false), SelfPid);
        Assert.AreEqual(PerchPollAction.TryReacquire, WindowPerchSelectionPolicy.DecideCleared(
            tracker.ClearReason, candidateMatchesClearedHandle: false));
        Assert.IsTrue(tracker.TryReacquire(Snapshot(hwnd: 0x40404, title: "新窗口 - 浏览器"), SelfPid));
    }

    [TestMethod]
    public void Tracker_Minimized_ClearsThenReacquiresOnRestore()
    {
        // R1 F-02 正例（保留）：最小化未销毁窗口，还原时同值句柄直接恢复。
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);

        Assert.AreEqual(PerchObservationOutcome.Cleared, tracker.Observe(Snapshot(minimized: true), SelfPid));
        Assert.AreEqual(PerchTargetClearReason.Minimized, tracker.ClearReason);

        // 从任务栏还原：同窗口身份重新验证通过。
        Assert.IsTrue(tracker.TryReacquire(Snapshot(), SelfPid));
        Assert.AreEqual(PerchTargetStatus.Tracking, tracker.Status);
    }

    [TestMethod]
    public void Tracker_Hidden_ClearsThenReacquiresOnRestore()
    {
        // 隐藏（未销毁）后重新可见：同值句柄身份匹配，允许直接恢复。
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);
        Assert.AreEqual(PerchObservationOutcome.Cleared, tracker.Observe(Snapshot(visible: false), SelfPid));

        Assert.IsTrue(tracker.TryReacquire(Snapshot(), SelfPid));
        Assert.AreEqual(PerchTargetStatus.Tracking, tracker.Status);
    }

    [TestMethod]
    public void Tracker_HiddenOrCloaked_ClearsWithHidden()
    {
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);

        Assert.AreEqual(PerchObservationOutcome.Cleared, tracker.Observe(Snapshot(visible: false), SelfPid));
        Assert.AreEqual(PerchTargetClearReason.Hidden, tracker.ClearReason);
    }

    [TestMethod]
    public void Tracker_TitleChange_SameWindowStillTracked()
    {
        // 标题会自然变化（如另存为），窗口类与进程 ID 不变即同一窗口，不是句柄复用。
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);

        var retitled = Snapshot(title: "a.txt - 记事本");
        Assert.AreEqual(PerchObservationOutcome.Updated, tracker.Observe(retitled, SelfPid));
        Assert.AreEqual(PerchTargetStatus.Tracking, tracker.Status);
    }

    [TestMethod]
    public void Tracker_NoLongerEligible_Clears()
    {
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);

        // 窗口仍在但标题被清空（失去资格）。
        Assert.AreEqual(PerchObservationOutcome.Cleared, tracker.Observe(Snapshot(title: ""), SelfPid));
        Assert.AreEqual(PerchTargetClearReason.NoLongerEligible, tracker.ClearReason);
    }

    [TestMethod]
    public void Tracker_Reset_ReturnsToNoTarget()
    {
        var tracker = new PerchTargetTracker();
        tracker.Select(Snapshot(), SelfPid);
        tracker.Observe(Snapshot(alive: false), SelfPid);

        tracker.Reset();
        Assert.AreEqual(PerchTargetStatus.NoTarget, tracker.Status);
        Assert.AreEqual(PerchTargetClearReason.None, tracker.ClearReason);
        Assert.IsNull(tracker.Target);
    }

    // ---------- P5A-001：落位 / 遮挡脱离 / 拖动抢占 ----------

    [TestMethod]
    public void Landing_TopEdgeCenter_IsLandingFoot()
    {
        // 落位脚底 = 可见外框上缘中点（物理像素）。
        var target = Snapshot(left: 100, top: 200, right: 500, bottom: 600);
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var footX, out var footY));
        Assert.AreEqual(300.0, footX);
        Assert.AreEqual(200.0, footY);
    }

    [TestMethod]
    public void Landing_NegativeCoordinates_Allowed()
    {
        // 左侧扩展屏负坐标：上缘中点允许为负。
        var target = Snapshot(left: -1920, top: -200, right: -100, bottom: 500);
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var footX, out var footY));
        Assert.AreEqual(-1010.0, footX);
        Assert.AreEqual(-200.0, footY);
    }

    [TestMethod]
    public void Landing_NullOrDegenerate_ReturnsFalse()
    {
        Assert.IsFalse(WindowPerchLandingPolicy.TryComputeLanding(null, out _, out _));
        Assert.IsFalse(WindowPerchLandingPolicy.TryComputeLanding(Snapshot(right: 100), out _, out _));
        Assert.IsFalse(WindowPerchLandingPolicy.TryComputeLanding(Snapshot(bottom: 100), out _, out _));
    }

    [TestMethod]
    public void Landing_IsAtLanding_WithinTolerance()
    {
        var target = Snapshot(left: 100, top: 200, right: 500, bottom: 600);
        Assert.IsTrue(WindowPerchLandingPolicy.IsAtLanding(303, 202, target, tolerancePx: 5));
        Assert.IsFalse(WindowPerchLandingPolicy.IsAtLanding(310, 202, target, tolerancePx: 5));
    }

    [TestMethod]
    public void Occlusion_FullyVisible_DoesNotDetach()
    {
        Assert.IsFalse(WindowPerchOcclusionPolicy.ShouldDetach(1.0));
    }

    [TestMethod]
    public void Occlusion_PartiallyOccluded_Detaches()
    {
        // 初版策略：上缘任一采样点被遮挡（fraction < 1）即脱离。
        Assert.IsTrue(WindowPerchOcclusionPolicy.ShouldDetach(0.999));
        Assert.IsTrue(WindowPerchOcclusionPolicy.ShouldDetach(0.5));
        Assert.IsTrue(WindowPerchOcclusionPolicy.ShouldDetach(0.0));
    }

    [TestMethod]
    public void Occlusion_CustomThreshold()
    {
        // 阈值可配置：允许半数可见仍栖息的松散策略（纯逻辑支持，初版用默认 1.0）。
        Assert.IsFalse(WindowPerchOcclusionPolicy.ShouldDetach(0.6, minimumVisibleFraction: 0.5));
        Assert.IsTrue(WindowPerchOcclusionPolicy.ShouldDetach(0.4, minimumVisibleFraction: 0.5));
    }

    [TestMethod]
    public void DragPreemption_CancelledHandle_NotAutoRebound()
    {
        // 拖动抢占取消的句柄不得被自动重选（松手后不回拉旧目标）。
        Assert.IsFalse(PerchDragPreemptionPolicy.ShouldAutoRebind(0x10101, 0x10101));
    }

    [TestMethod]
    public void DragPreemption_OtherHandle_MayAutoRebind()
    {
        Assert.IsTrue(PerchDragPreemptionPolicy.ShouldAutoRebind(0x10101, 0x50505));
        Assert.IsTrue(PerchDragPreemptionPolicy.ShouldAutoRebind(null, 0x10101));
    }

    [TestMethod]
    public void DragPreemption_ExplicitSelection_AlwaysAllowed()
    {
        // 明确的显式新选择是独立输入：即使与被取消句柄相同也允许。
        Assert.IsTrue(PerchDragPreemptionPolicy.ShouldAllowExplicitSelection(0x10101, 0x10101, isExplicitNewSelection: true));
        // 非显式选择则按自动重选策略拒绝同值句柄。
        Assert.IsFalse(PerchDragPreemptionPolicy.ShouldAllowExplicitSelection(0x10101, 0x10101, isExplicitNewSelection: false));
    }

    [TestMethod]
    public void DragPreemption_CancelledHandle_NotReboundAcrossPersistentPolling()
    {
        // 持续轮询语义的确定性复现：拖动取消后同值句柄出现在枚举结果中，
        // 持续轮询也不得重新绑定。
        for (var poll = 0; poll < 25; poll++)
        {
            Assert.IsFalse(PerchDragPreemptionPolicy.ShouldAutoRebind(0x10101, 0x10101), $"poll #{poll}");
        }
    }

    // ---------- P5A-001 R1 整改：可见落位（F-01）/ 闸门只读预检（F-02/F-04） ----------

    [TestMethod]
    public void LandingWithPetRect_FitsVisibleArea_Succeeds()
    {
        // 完整宠物矩形容得下：目标上缘 200、宠物 180 高 → 顶部 20 在屏内。
        var target = Snapshot(left: 100, top: 200, right: 500, bottom: 600);
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(
            target, 200, 180, 0, 0, 2560, 1440, out var footX, out var footY));
        Assert.AreEqual(300.0, footX);
        Assert.AreEqual(200.0, footY);
    }

    [TestMethod]
    public void LandingWithPetRect_MaximizedTopEdge_NoRoomAbove_Rejected()
    {
        // R1 F-01 反例：最大化后目标上缘为 0，宠物矩形 [x, -180, x, 0]
        // 完全出屏 → 拒绝落位（不得移动、不得报 landed/follow）。
        var maximized = Snapshot(left: 0, top: 0, right: 2560, bottom: 1400);
        Assert.IsFalse(WindowPerchLandingPolicy.TryComputeLanding(
            maximized, 200, 180, 0, 0, 2560, 1440, out _, out _));
    }

    [TestMethod]
    public void LandingWithPetRect_TightFit_AtExactVisibleTop_Succeeds()
    {
        // 上沿恰好容下（宠物顶=可见区顶）为边界正例：恰好贴住仍可落位。
        var target = Snapshot(left: 100, top: 180, right: 500, bottom: 600);
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(
            target, 200, 180, 0, 0, 2560, 1440, out _, out _));
    }

    [TestMethod]
    public void LandingWithPetRect_HorizontalOverflow_Rejected()
    {
        // 水平内含反例：落脚点贴近可见区右缘，宠物右半出屏 → 拒绝。
        var target = Snapshot(left: 2460, top: 200, right: 2560, bottom: 600);
        Assert.IsFalse(WindowPerchLandingPolicy.TryComputeLanding(
            target, 200, 180, 0, 0, 2560, 1440, out _, out _));
        // 负坐标（左侧扩展屏）同样适用：落脚点太靠左，宠物左半出屏 → 拒绝。
        var negTarget = Snapshot(left: -1920, top: 200, right: -1750, bottom: 600);
        Assert.IsFalse(WindowPerchLandingPolicy.TryComputeLanding(
            negTarget, 200, 180, -1920, 0, 2560, 1440, out _, out _));
    }

    [TestMethod]
    public void LandingWithPetRect_AtRightVisibleEdge_ExactFit_Succeeds()
    {
        // 水平边界正例：宠物右缘恰好贴住可见区右缘仍可落位。
        var target = Snapshot(left: 2360, top: 200, right: 2560, bottom: 600);
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(
            target, 200, 180, 0, 0, 2560, 1440, out _, out _));
    }

    [TestMethod]
    public void LandingWithPetRect_DegeneratePetOrVisibleArea_Rejected()
    {
        var target = Snapshot(left: 100, top: 200, right: 500, bottom: 600);
        // 宠物尺寸非正 → 拒绝。
        Assert.IsFalse(WindowPerchLandingPolicy.TryComputeLanding(target, 0, 180, 0, 0, 2560, 1440, out _, out _));
        Assert.IsFalse(WindowPerchLandingPolicy.TryComputeLanding(target, 200, 0, 0, 0, 2560, 1440, out _, out _));
        // 可见区退化 → 拒绝。
        Assert.IsFalse(WindowPerchLandingPolicy.TryComputeLanding(target, 200, 180, 100, 100, 100, 1440, out _, out _));
        // 目标退化 → 拒绝（旧 2 参数重载语义保持）。
        Assert.IsFalse(WindowPerchLandingPolicy.TryComputeLanding(
            Snapshot(right: 100), 200, 180, 0, 0, 2560, 1440, out _, out _));
    }

    [TestMethod]
    public void UpdateGate_PeekDoesNotConsumeBaseline()
    {
        // R1 F-02/F-04：PeekShouldReport 只读预检，不消费基线——
        // 未提交成功前反复 Peek 结果稳定；提交成功后 ShouldReport 消费。
        var gate = new PerchUpdateGate();
        gate.Reset(0, 0, 800, 500);

        Assert.IsFalse(gate.PeekShouldReport(0, 0, 800, 500));
        // 目标移动：Peek 应报但不消费。
        Assert.IsTrue(gate.PeekShouldReport(100, 50, 900, 550));
        Assert.IsTrue(gate.PeekShouldReport(100, 50, 900, 550));
        // 基线未变：ShouldReport 消费后前移。
        Assert.IsTrue(gate.ShouldReport(100, 50, 900, 550));
        Assert.IsFalse(gate.PeekShouldReport(100, 50, 900, 550));
        Assert.IsFalse(gate.ShouldReport(100, 50, 900, 550));
    }

    [TestMethod]
    public void UpdateGate_PeekWithoutBaseline_Reports()
    {
        // 尚未建立基线时 Peek 与 ShouldReport 一致视为应上报。
        var gate = new PerchUpdateGate();
        Assert.IsTrue(gate.PeekShouldReport(0, 0, 800, 500));
        Assert.IsTrue(gate.ShouldReport(0, 0, 800, 500));
        Assert.IsFalse(gate.PeekShouldReport(0, 0, 800, 500));
    }
}
