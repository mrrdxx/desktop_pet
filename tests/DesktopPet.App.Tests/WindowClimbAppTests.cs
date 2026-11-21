using DesktopPet.App;
using DesktopPet.App.Persistence;
using DesktopPet.App.Platform;
using DesktopPet.Core.Persistence;
using DesktopPet.Core.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P5B-001（R-08）：窗口有限攀爬首版（隔离预览）的 App 级测试。
///
/// 覆盖：
/// - 起攀校验链：太远拒绝（不移动不改变状态）、不合格目标拒绝、
///   落点被遮挡拒绝、位置被占用拒绝（RejectedBusy）、
///   已在攀爬中拒绝（RejectedAlreadyActive）；
/// - 爬行推进：沿侧边→上缘折线逐步推进（climb-step 事件、位置单调）；
/// - 到达落位：结束路线并经 Perch.TrySelectExplicit 交接栖息
///   （落位脚点=上缘中点，无可见跳动）；
/// - 目标移动：每 tick 按最新快照重建路线（落位跟随最新上缘中点）；
/// - 目标最小化：取消路线并回到可见区域（climb-cancelled: target-*）；
/// - 目标真实关闭（句柄销毁 → DeadSnapshot → WindowGone 取消，回可见区）；
/// - 用户拖动：立即接管结束路线（climb-drag-preempted，不恢复位置）；
/// - 暂停：同步取消（climb-cancelled: paused），恢复后不续接；
/// - 退出：恢复爬行前位置（存档为休息位）；
/// - 生产退出存档往返：FlushFinalState → CancelForExit → SaveCoordinator
///   落盘爬行前坐标；新窗口重载后不续跑旧路线（零写入）；
/// - 位置所有权：攀爬期间栖息初选让出（TrySelectExplicit 被拒）。
///
/// 伪造句柄的落位/遮挡校验经测试缝强制（只替换采样来源，
/// 落位/提交/事件路径不变，P5A-002/P5A-003 同款缝）。
/// 攀爬动作为临时表现（idle→curious→idle），不做专用素材断言。
/// </summary>
[TestClass]
public class WindowClimbAppTests
{
    // 宠物窗口 120×120 DIP；未 Show 的测试窗口 HWND=0 → DPI 1.0（DIP=物理像素）。
    private const double PetLeft = 600;
    private const double PetTop = 500;
    private const double PetWidth = 120;
    private const double PetHeight = 120;

    // 目标可见外框（物理像素）：脚点(660,620) 距外框 40px（≤200 允许起攀）。
    private const int TLeft = 700;
    private const int TTop = 420;
    private const int TRight = 1000;
    private const int TBottom = 670;

    private static MainWindow? TryCreateMainWindow()
    {
        try
        {
            return new MainWindow();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "WindowClimbAppTests: MainWindow construction failed: {0}", ex);
            return null;
        }
    }

    private static ExternalWindowSnapshot Snapshot(
        long hwnd, int left = TLeft, int top = TTop, int right = TRight,
        int bottom = TBottom, bool minimized = false)
        => new(hwnd, "climb-target", "FakeClass", 5000, "fakeproc",
            true, true, minimized, false, false, left, top, right, bottom);

    /// <summary>统一测试布景：宠物位置 + 攀爬/栖息测试缝 + 事件流。</summary>
    private static PetWindowClimb Setup(MainWindow window, List<PetClimbEvent> events)
    {
        window.Left = PetLeft;
        window.Top = PetTop;
        var climb = window.Climb;
        climb.TestLandingOcclusionSampler = (_, _, _) => (1.0, new List<string>());
        climb.ClimbEvent += events.Add;
        window.Perch.TestLandingOcclusionSampler = (_, _, _) => (1.0, new List<string>());
        return climb;
    }

    private static void AssertRestPosition(MainWindow window)
    {
        Assert.AreEqual(PetLeft, window.Left, 0.001, "拒绝路径不得移动宠物。");
        Assert.AreEqual(PetTop, window.Top, 0.001, "拒绝路径不得移动宠物。");
    }

    [STATestMethod]
    public void Climb_TryStart_TooFar_RejectedWithoutSideEffects()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);

            // 目标外框距脚点 740px（>200）：起攀距离拒绝。
            var far = Snapshot(0x88888, left: 1400, top: 420, right: 1700, bottom: 670);
            var result = climb.TryStart(far, (uint)Environment.ProcessId, 0);

            Assert.AreEqual(PetClimbStartResult.RejectedTooFar, result);
            Assert.IsFalse(climb.IsActive, "拒绝不得进入攀爬。");
            Assert.AreEqual(0, events.Count, "拒绝不得发事件。");
            AssertRestPosition(window);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_TryStart_IneligibleTarget_Rejected()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);

            var minimized = Snapshot(0x88888, minimized: true);
            var result = climb.TryStart(minimized, (uint)Environment.ProcessId, 0);

            Assert.AreEqual(PetClimbStartResult.RejectedIneligible, result);
            Assert.IsFalse(climb.IsActive);
            Assert.AreEqual(0, events.Count);
            AssertRestPosition(window);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_TryStart_LandingOccluded_Rejected()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            // 顶部落位脚点被完全遮挡。
            climb.TestLandingOcclusionSampler = (_, _, _) => (0.0, new List<string> { "occluder" });

            var result = climb.TryStart(Snapshot(0x88888), (uint)Environment.ProcessId, 0);

            Assert.AreEqual(PetClimbStartResult.RejectedOccluded, result);
            Assert.IsFalse(climb.IsActive);
            Assert.AreEqual(0, events.Count);
            AssertRestPosition(window);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_TryStart_WhilePositionOwned_RejectedBusy()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            // 位置被物理活动占用（互斥核心旗）。
            typeof(MainWindow).GetField("_isPhysicsActive",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(window, true);

            var result = climb.TryStart(Snapshot(0x88888), (uint)Environment.ProcessId, 0);

            Assert.AreEqual(PetClimbStartResult.RejectedBusy, result);
            Assert.IsFalse(climb.IsActive);
            Assert.AreEqual(0, events.Count);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_AdvancesAlongRoute_ThenCompletesAndHandsOffToPerch()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x99999);
            climb.TestSnapshotSource = _ => snapshot;

            // 第一次起攀尝试成功。
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            Assert.IsTrue(climb.IsActive);
            Assert.AreEqual("climb-started", events[0].Type);
            // 已在攀爬中：重复起攀拒绝。
            Assert.AreEqual(PetClimbStartResult.RejectedAlreadyActive,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));

            // 攀爬期间栖息初选让出（位置所有权互斥）。
            Assert.IsFalse(window.Perch.TrySelectExplicit(snapshot, (uint)Environment.ProcessId),
                "攀爬期间栖息初选必须被位置所有权守卫拒绝。");

            // 部分推进：dt=100ms、速度 0.45px/ms → 45px：侧边段 40px 用完，
            // 余 5px 沿侧边向上 → 脚点 (700, 615)。
            climb.TestSpeedPxPerMs = WindowClimbPolicy.DefaultSpeedPxPerMs;
            climb.Tick(100);
            Assert.IsTrue(climb.IsActive, "45px 后路线必须仍在进行。");
            Assert.AreEqual("climb-step", events[1].Type);
            Assert.AreEqual(700 - PetWidth / 2, window.Left, 0.001, "侧边推进 45px 后的窗口位置。");
            Assert.AreEqual(615 - PetHeight, window.Top, 0.001);

            // 大步到达：advance 超过剩余折线 → 收敛到落位脚点并交接栖息。
            climb.TestSpeedPxPerMs = 50.0;
            climb.Tick(200);

            Assert.IsFalse(climb.IsActive, "到达后必须结束路线。");
            Assert.IsTrue(window.Perch.IsActive, "到达后必须交接栖息绑定。");
            Assert.AreEqual(0x99999L, window.Perch.TargetHwndValue);
            var last = events[^1];
            Assert.AreEqual("climb-completed", last.Type);
            // 落位脚点 = 可见外框上缘中点：(850, 420)；窗口左上 = (790, 300)。
            Assert.AreEqual(850 - PetWidth / 2, window.Left, 0.001, "落位必须无可见跳动。");
            Assert.AreEqual(420 - PetHeight, window.Top, 0.001);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_MovingTarget_RouteRebuiltEachTick_AndFollowsLatestTopMid()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x99999);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TestSpeedPxPerMs = 50.0;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));

            // 目标右移 100px：下一 tick 按最新快照重建路线。
            snapshot = Snapshot(0x99999, left: TLeft + 100, right: TRight + 100);
            climb.Tick(100);

            Assert.IsFalse(climb.IsActive, "大步推进后必须到达。");
            Assert.IsTrue(window.Perch.IsActive);
            // 最新上缘中点 = (950, 420) → 窗口左上 = (890, 300)。
            Assert.AreEqual(950 - PetWidth / 2, window.Left, 0.001,
                "目标移动后落位必须跟随最新上缘中点。");
            Assert.AreEqual(420 - PetHeight, window.Top, 0.001);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_TargetMinimizedDuringRoute_CancelsAndReturnsToVisibleArea()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x99999);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TestSpeedPxPerMs = WindowClimbPolicy.DefaultSpeedPxPerMs;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            climb.Tick(100); // 部分推进。
            Assert.IsTrue(climb.IsActive, "前置：取消前路线仍在进行。");

            // 目标最小化：重观测清除目标 → 取消并回到可见区域。
            snapshot = Snapshot(0x99999, minimized: true);
            climb.Tick(200);

            Assert.IsFalse(climb.IsActive, "目标最小化必须取消路线。");
            Assert.IsFalse(window.Perch.IsActive, "取消不得交接栖息。");
            var cancelled = events.FirstOrDefault(e => e.Type == "climb-cancelled");
            Assert.IsNotNull(cancelled, "取消必须发 climb-cancelled 事件。");
            Assert.IsTrue(cancelled!.Reason!.StartsWith("target-", StringComparison.Ordinal),
                "取消原因必须是 target-*（重观测清除）。实际：" + cancelled.Reason);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_DragPreempts_ImmediatelyEndsRouteWithoutRestore()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x99999);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TestSpeedPxPerMs = WindowClimbPolicy.DefaultSpeedPxPerMs;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            climb.Tick(100); // 推进到 (705, 620)。
            var leftAfterStep = window.Left;
            var topAfterStep = window.Top;

            // 用户拖动：立即接管，结束路线、不恢复位置。
            climb.CancelForDrag();

            Assert.IsFalse(climb.IsActive, "拖动必须立即结束路线。");
            Assert.AreEqual("climb-drag-preempted", events[^1].Type);
            Assert.AreEqual(leftAfterStep, window.Left, 0.001, "拖动接管不得移动宠物。");
            Assert.AreEqual(topAfterStep, window.Top, 0.001);
            Assert.IsFalse(window.Perch.IsActive);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_PauseCancelsRoute_ResumeDoesNotResume()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x99999);
            climb.TestSnapshotSource = _ => snapshot;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            Assert.IsTrue(climb.IsActive, "前置：暂停前路线激活。");

            // 暂停入口（托盘/菜单共用）：必须同步取消。
            window.PauseAnimation();

            Assert.IsFalse(climb.IsActive, "暂停必须同步结束路线。");
            var cancelled = events.FirstOrDefault(e => e.Type == "climb-cancelled");
            Assert.IsNotNull(cancelled);
            Assert.AreEqual("paused", cancelled!.Reason);

            // 恢复后不续接旧路线。
            window.ResumeAnimation();
            Assert.IsFalse(climb.IsActive, "恢复后不得续接旧路线。");
            climb.Tick(1000);
            Assert.IsFalse(climb.IsActive, "恢复后 tick 不得重新推进旧路线。");
            Assert.IsFalse(window.Perch.IsActive);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_ExitRestoresPreClimbPosition()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x99999);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TestSpeedPxPerMs = WindowClimbPolicy.DefaultSpeedPxPerMs;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            climb.Tick(100); // 路线中间位置 (705, 620)。
            Assert.IsTrue(climb.IsActive, "前置：退出前路线仍在进行。");

            // 退出：结束路线并恢复爬行前位置（存档为休息位）。
            climb.CancelForExit();

            Assert.IsFalse(climb.IsActive);
            Assert.AreEqual(PetLeft, window.Left, 0.001, "退出必须恢复爬行前位置。");
            Assert.AreEqual(PetTop, window.Top, 0.001);
            Assert.IsFalse(window.Perch.IsActive);
        }
        finally { window.Close(); }
    }

    // ---- P5B-001 R1 监理反例（原样纳入：Reviewer_Climb_ 前缀、断言语义不变）----

    [STATestMethod]
    public void Reviewer_Climb_AlreadyPerched_MustRejectWithoutChangingBinding()
    {
        var w = new MainWindow();
        try {
            var events = new List<PetClimbEvent>(); var c = Setup(w, events);
            var target = Snapshot(0x99999);
            Assert.IsTrue(w.Perch.TrySelectExplicit(target, (uint)Environment.ProcessId));
            Assert.IsTrue(w.Perch.IsActive);
            Assert.AreEqual(PetClimbStartResult.RejectedBusy, c.TryStart(target,(uint)Environment.ProcessId,0));
        } finally { w.Close(); }
    }
    [STATestMethod]
    public void Reviewer_Climb_Recovery_MustCancelRoute()
    {
        var w = new MainWindow();
        try {
            var c = Setup(w, new List<PetClimbEvent>());var target=Snapshot(0x99999);
            c.TestSnapshotSource=_=>target;
            Assert.AreEqual(PetClimbStartResult.Started,c.TryStart(target,(uint)Environment.ProcessId,0));
            c.Tick(100);w.RecoverPet();
            Assert.IsFalse(c.IsActive,"找回应立即结束攀爬，后续不得拉回目标");
        } finally { w.Close(); }
    }
    [STATestMethod]
    public void Reviewer_Climb_MustNotShortcutPolylineWhenDirectDistanceFitsBudget()
    {
        var w = new MainWindow();
        try {
            var c = Setup(w,new List<PetClimbEvent>()); var target=Snapshot(0x99999);
            c.TestSnapshotSource=_=>target;
            Assert.AreEqual(PetClimbStartResult.Started,c.TryStart(target,(uint)Environment.ProcessId,0));
            // foot=(660,620); landing=(850,420), Euclidean=275.86, route=40+200+150=390.
            c.TestSpeedPxPerMs=0.45;c.Tick(667); // budget=300.15, still 89.85 along polyline remaining.
            Assert.IsTrue(c.IsActive,"预算尚未走完390px折线，不应斜线瞬移交接");
        } finally { w.Close(); }
    }

    [STATestMethod]
    public void Reviewer_Climb_LocalSocialActive_MustRejectStart()
    {
        var w=new MainWindow();
        try {
            var c=Setup(w,new List<PetClimbEvent>());
            typeof(MainWindow).GetField("_isSocialActive",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(w,true);
            Assert.IsFalse(w.IsClimbWriteAllowed,"社交已持有时首次起攀不得获准");
        } finally {w.Close();}
    }
    [STATestMethod]
    public void Reviewer_Climb_LongStall_MustDiscardBacklog()
    {
        var w=new MainWindow();
        try {
            var c=Setup(w,new List<PetClimbEvent>());var target=Snapshot(0x99999);
            c.TestSnapshotSource=_=>target;
            Assert.AreEqual(PetClimbStartResult.Started,c.TryStart(target,(uint)Environment.ProcessId,0));
            c.Tick(10000);
            Assert.IsTrue(c.IsActive,"10秒停顿不能一次消费全部积压并瞬移到顶");
        }finally{w.Close();}
    }

    // ---- P5B-001 R1 F-01 整改新增用例 ----

    [STATestMethod]
    public void Climb_FirstStart_WhenUnaffected_Succeeds()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x88888);
            climb.TestSnapshotSource = _ => snapshot;
            // 无栖息、无本地/共享社交、无核心冲突：首次起攀必须成功。
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            Assert.IsTrue(climb.IsActive);
            Assert.AreEqual("climb-started", events[0].Type);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_TryStart_WhileSharedSocialSessionActive_RejectedBusy()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            // 跨窗口共享会话：协调器已持有一个含 bear-1 的活跃会话
            // （本地 _isSocialActive 仍 false，只有共享会话生效）。
            var coordinator = new Core.Social.SocialCoordinator();
            coordinator.TestSeedSession(new Core.Social.SocialSession(
                "climb-shared-test", "bear-2", "bear-1",
                Core.Social.SocialPhase.Noticing, 0, 0));
            window.TestSocialCoordinator = coordinator;

            var result = climb.TryStart(Snapshot(0x88888), (uint)Environment.ProcessId, 0);

            Assert.AreEqual(PetClimbStartResult.RejectedBusy, result,
                "共享社交会话已占有时首次起攀必须 RejectedBusy。");
            Assert.IsFalse(climb.IsActive);
            Assert.AreEqual(0, events.Count, "拒绝不得发事件。");
            AssertRestPosition(window);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_Recover_ThenTickAndTargetMove_NoWrites()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x99999);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TestSpeedPxPerMs = WindowClimbPolicy.DefaultSpeedPxPerMs;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            climb.Tick(100);
            Assert.IsTrue(climb.IsActive, "前置：找回前路线激活。");
            var stepCountAtRecover = events.Count(e => e.Type == "climb-step");

            // 找回：解除栖息 + 同步取消路线，并移动到主屏可见区。
            window.RecoverPet();
            Assert.IsFalse(climb.IsActive, "找回必须结束路线。");
            Assert.AreEqual("climb-cancelled", events[^1].Type);
            Assert.AreEqual("recovered", events[^1].Reason);
            var leftAfterRecover = window.Left;
            var topAfterRecover = window.Top;

            // 找回后推进 Tick：不得重建路线、不得回拉。
            climb.Tick(1000);
            Assert.IsFalse(climb.IsActive, "找回后 tick 不得重新激活路线。");
            Assert.AreEqual(leftAfterRecover, window.Left, 0.001, "找回后 tick 不得移动宠物。");
            Assert.AreEqual(topAfterRecover, window.Top, 0.001);

            // 找回后旧目标移动：也不得回拉（每 tick 重观测路径已停）。
            snapshot = Snapshot(0x99999, left: TLeft + 100, right: TRight + 100);
            climb.Tick(500);
            Assert.AreEqual(leftAfterRecover, window.Left, 0.001, "找回后旧目标移动不得回拉。");
            Assert.AreEqual(topAfterRecover, window.Top, 0.001);
            var stepCountAfter = events.Count(e => e.Type == "climb-step");
            Assert.AreEqual(stepCountAtRecover, stepCountAfter,
                "找回后不得再发 climb-step 推进事件。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_WhileActive_NonClimbFootWrite_Rejected()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x99999);
            climb.TestSnapshotSource = _ => snapshot;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            climb.Tick(100);
            Assert.IsTrue(climb.IsActive);
            var leftWhileActive = window.Left;
            var topWhileActive = window.Top;

            // 攀爬持有时，非攀爬位移（栖息跟随/测试直接提交，fromClimb=false）
            // 必须被守卫拒绝且不移动宠物。
            Assert.IsFalse(window.ApplyPerchFoot(new Core.Geometry.PetWorldPosition(1234, 1000)),
                "攀爬持有位置时非攀爬位移不得提交。");
            Assert.AreEqual(leftWhileActive, window.Left, 0.001, "被拒位移不得移动宠物。");
            Assert.AreEqual(topWhileActive, window.Top, 0.001);
            Assert.IsTrue(climb.IsActive, "被拒的非攀爬位移不得结束攀爬路线。");
        }
        finally { window.Close(); }
    }

    // ---- P5B-001 R1 F-02 整改新增用例 ----

    [STATestMethod]
    public void Climb_FixedStep_AdvancesAlongPolyline_ThenLastStepReachesLanding()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x99999);
            climb.TestSnapshotSource = _ => snapshot;
            // 速度 0.45px/ms × 步长上限 200ms = 90px/步；折线 390px
            // （40 进左边 + 200 上爬 + 150 上缘水平）。
            climb.TestSpeedPxPerMs = WindowClimbPolicy.DefaultSpeedPxPerMs;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            climb.Tick(200); // dt=200 → 90px：40 进左边 + 50 上爬 → (700,570)
            Assert.IsTrue(climb.IsActive);
            // 1 步：40(→700,620) + 50(→700,570)。
            Assert.AreEqual(700 - PetWidth / 2, window.Left, 0.001);
            Assert.AreEqual(570 - PetHeight, window.Top, 0.001);

            climb.Tick(400); // dt=200 → 再 90px 上爬 → (700,480)
            Assert.IsTrue(climb.IsActive);
            Assert.AreEqual(700 - PetWidth / 2, window.Left, 0.001);
            Assert.AreEqual(480 - PetHeight, window.Top, 0.001);

            climb.Tick(600); // dt=200 → 走完上爬余 60 + 水平 30 → (730,420)
            Assert.IsTrue(climb.IsActive);
            // 第二段 200px 用毕还余 30：沿上缘水平 (700→730, 420)。
            Assert.AreEqual(730 - PetWidth / 2, window.Left, 0.001);
            Assert.AreEqual(420 - PetHeight, window.Top, 0.001);

            climb.Tick(800); // dt=200 → 再 90px 水平 → (820,420)
            Assert.IsTrue(climb.IsActive);
            // 上缘已走 30，剩 120；本步 90 → (820, 420)。
            Assert.AreEqual(820 - PetWidth / 2, window.Left, 0.001);
            Assert.AreEqual(420 - PetHeight, window.Top, 0.001);

            // 末次步进：剩余 30px ≤ 步长 90 → 到达交接，脚点收敛到落位点。
            climb.Tick(1000); // dt=200：末步只消费到剩余 30px，不多走不跳过
            Assert.IsFalse(climb.IsActive, "最后一步必须结束路线。");
            Assert.IsTrue(window.Perch.IsActive, "到达后必须交接栖息。");
            Assert.AreEqual("climb-completed", events[^1].Type);
            Assert.AreEqual(850 - PetWidth / 2, window.Left, 0.001,
                "末次步进后脚点必须恰在落位点，无跳跃。");
            Assert.AreEqual(420 - PetHeight, window.Top, 0.001);
            // 交接前后脚点一致：completed 事件的 foot 即落位脚点。
            Assert.AreEqual(850.0, events[^1].FootX, 0.001, "completed foot 必须为上缘中点。");
            Assert.AreEqual(420.0, events[^1].FootY, 0.001);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Climb_ClockRollback_DoesNotReplayOrDuplicateSteps()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x99999);
            climb.TestSnapshotSource = _ => snapshot;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            climb.Tick(10000); // 积压：只消费 200ms → 90px。
            Assert.IsTrue(climb.IsActive);
            Assert.AreEqual(700 - PetWidth / 2, window.Left, 0.001);
            Assert.AreEqual(570 - PetHeight, window.Top, 0.001);

            // 首次大步消费：10s 停顿仅消费 200ms 预算 → 1 步，不补计。
            Assert.AreEqual(1, events.Count(e => e.Type == "climb-step"),
                "积压只消费 200ms，不得一次补完。");

            // 时钟不变（dt=0）：不推进、不重复消费上一步剩余。
            climb.Tick(10000);
            Assert.IsTrue(climb.IsActive);
            Assert.AreEqual(700 - PetWidth / 2, window.Left, 0.001);
            Assert.AreEqual(570 - PetHeight, window.Top, 0.001);
            Assert.AreEqual(1, events.Count(e => e.Type == "climb-step"),
                "dt=0 不得重复消费积压（_lastTickMs 已更新）。");

            // 时钟前进到 10200（相对上次 tick 200ms）：再走 90px → 第 2 步。
            climb.Tick(10200);
            Assert.IsTrue(climb.IsActive);
            Assert.AreEqual(700 - PetWidth / 2, window.Left, 0.001);
            Assert.AreEqual(480 - PetHeight, window.Top, 0.001);
            Assert.AreEqual(2, events.Count(e => e.Type == "climb-step"),
                "时钟前进 dt=200 后仅补 1 步。");
        }
        finally { window.Close(); }
    }

    // ---- P5B-001 R1 F-04 整改新增用例 ----

    /// <summary>
    /// 生产退出存档往返（P5B-001 R1 F-04）：
    /// 与 WindowPerchOwnershipTests.Perch_ExitArchiveRoundTrip_* 同一模式——
    /// 走真实 CreatePetWindow → TryStart 记录爬行前位置 → Tick 推进到路线
    /// 中间 → ShutdownMainWindowForTest（FlushFinalState →
    /// RestoreProbePositionForExit → CancelForExit 恢复爬行前位置 →
    /// MarkStateDirty → SaveCoordinator 落盘）。
    /// 断言：落盘坐标必须是爬行前位置（休息位），非路线临时位；
    /// 新窗口从重载状态创建后不续跑旧路线（零写入）。
    /// </summary>
    [STATestMethod]
    public void Climb_ExitArchiveRoundTrip_SavesPreClimbPosition_NewWindowNoOldBinding()
    {
        var app = (App?)System.Windows.Application.Current ?? new App();
        string? dir = null;
        try
        {
            dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"P5B_R1F04_{Guid.NewGuid():N}");
            app.TestSetupIsolatedSave(dir);
            // 存档休息位 = Setup 默认宠物位（PetLeft=600, PetTop=500），
            // 保证 TryStart 记录的前位置与存档基准一致。
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive,
                        Grievance = 40, WindowLeft = PetLeft, WindowTop = PetTop },
            };

            app.CreatePetWindow(app.TestState.Pets[0]);
            var w = app.FindWindowByPetId("bear-1")!;
            var events = new List<PetClimbEvent>();
            var climb = Setup(w, events);
            var snapshot = Snapshot(0x77777);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TestSpeedPxPerMs = WindowClimbPolicy.DefaultSpeedPxPerMs;

            // 真实起攀：TryStart 记录爬行前位置（=休息位 600,500）。
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            Assert.IsTrue(climb.IsActive);

            // 推进到路线中间：临时攀爬位（存档不得保存此坐标）。
            climb.Tick(100); // 45px：40 进侧边 + 5 上爬。
            Assert.IsTrue(climb.IsActive, "前置：退出前路线必须仍在进行。");
            Assert.AreNotEqual(PetLeft, w.Left, 0.001,
                "前置：路线中窗口必须已离开休息位。");

            // 正常退出（真实退出保存链路）。
            app.ShutdownMainWindowForTest();

            // 重载存档：必须保存爬行前坐标（休息位），非路线临时位。
            var archive = new ArchiveService(dir);
            var (restored, _, _) = archive.LoadState();
            Assert.AreEqual(1, restored.Pets.Count, "退出必须保留宠物。");
            Assert.AreEqual(PetLeft, restored.Pets[0]!.WindowLeft, 0.001,
                "落盘坐标必须是爬行前位置。");
            Assert.AreEqual(PetTop, restored.Pets[0]!.WindowTop, 0.001,
                "落盘坐标必须是爬行前位置。");

            // 新窗口从重载状态创建：不续跑旧路线。
            app.CreatePetWindow(restored.Pets[0]!);
            var w2 = app.FindWindowByPetId("bear-1")!;
            var climb2 = w2.Climb;
            Assert.IsFalse(climb2.IsActive, "新窗口不得续跑旧路线。");
            Assert.IsNull(climb2.TargetHwndValue, "新窗口不得持有旧目标。");

            // 驱动一轮 tick：零写入（路线只在内存，重启后不存在）。
            var left2 = w2.Left;
            var top2 = w2.Top;
            climb2.Tick(1000);
            Assert.AreEqual(left2, w2.Left, 0.001, "新窗口 tick 不得写位置。");
            Assert.AreEqual(top2, w2.Top, 0.001, "新窗口 tick 不得写位置。");
            Assert.IsFalse(climb2.IsActive, "无效 tick 不得激活路线。");
        }
        finally
        {
            try { app.TestCleanup(); }
            catch { /* already cleaned by shutdown */ }
        }
    }

    /// <summary>
    /// 路线中目标真实关闭（P5B-001 R1 F-04）：
    /// TestSnapshotSource 返回 null 等价于句柄已销毁（生产
    /// WindowPerchInterop.SnapshotWindow 对已销毁句柄返回 null）——
    /// 生产 Tick 重观测路径构造 DeadSnapshot → PerchTargetTracker.Observe
    /// 判定 IsWindowAlive=false → WindowGone 清除 → CancelCore("target-WindowGone")
    /// → 回可见区 + climb-cancelled(target-WindowGone)。
    /// 证据层次：本测试覆盖「重观测 → 失效判定 → 取消 → 不再写位置」完整
    /// 判定链；「回可见区实机钳制」由 M-17 驱动真实关闭目标场景捕捉。
    /// </summary>
    [STATestMethod]
    public void Climb_TargetClosedDuringRoute_DeadSnapshot_CancelsAndStopsWrites()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = Snapshot(0x99999);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TestSpeedPxPerMs = WindowClimbPolicy.DefaultSpeedPxPerMs;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            climb.Tick(100); // 部分推进（发 1 次 climb-step）。
            Assert.IsTrue(climb.IsActive, "前置：目标关闭前路线仍在进行。");
            var stepsBeforeClose = events.Count(e => e.Type == "climb-step");

            // 目标窗口真实关闭：采样返回 null（句柄销毁）。
            climb.TestSnapshotSource = _ => null;
            climb.Tick(200);

            Assert.IsFalse(climb.IsActive, "目标关闭必须取消路线。");
            Assert.IsFalse(window.Perch.IsActive, "取消不得交接栖息。");
            Assert.IsNull(climb.TargetHwndValue, "取消后不得残留目标。");
            var cancelled = events.FirstOrDefault(e => e.Type == "climb-cancelled");
            Assert.IsNotNull(cancelled, "取消必须发 climb-cancelled 事件。");
            Assert.AreEqual("target-WindowGone", cancelled!.Reason,
                "句柄销毁的取消原因必须是 target-WindowGone。实际：" + cancelled.Reason);
            Assert.AreEqual(stepsBeforeClose, events.Count(e => e.Type == "climb-step"),
                "取消路径不得再发推进事件（步数须停留在取消前）。");

            // 取消后 tick 不再移动窗口（无旧目标、无路线）。
            var leftAfter = window.Left;
            var topAfter = window.Top;
            climb.Tick(1000);
            Assert.AreEqual(leftAfter, window.Left, 0.001, "取消后 tick 不得再移动宠物。");
            Assert.AreEqual(topAfter, window.Top, 0.001);
            Assert.IsFalse(climb.IsActive);
        }
        finally { window.Close(); }
    }
}
