using System.Reflection;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Geometry;
using DesktopPet.Core.Input;
using DesktopPet.Core.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P5B-002：窗口攀爬正式入口（右键「Climb on Window」→ 拾取点击 →
/// 既有 PetWindowClimb 路线 → 交接栖息）的 App 级测试。
///
/// 覆盖：
/// - 菜单命令进入攀爬拾取模式（与原生菜单同一 BeginInvoke 分发路径）；
/// - 拾取中左键点击合格近距目标 → TryStart started + 攀爬激活；
/// - 同一拾取状态机的取消/拒绝路径对攀爬意图同样生效：点自身取消、
///   不合格目标拒绝、超距拒绝（不移动）、落点遮挡拒绝（不移动）、
///   超时取消、位置被占用不入拾取、开菜单/暂停取消；
/// - 攀爬进行中的重复进入拒绝边界（rejected-active 不产生）——
///   攀爬激活后拾取已退出，走 Climb.TryStart 的 RejectedAlreadyActive 分支；
/// - 平行入口「Perch on Window」回归不变（selected）；
/// - 完整关键链：菜单进入 → 点击 → 沿既有路线推进 → 到达交接栖息。
///
/// 布景沿用 WindowClimbAppTests：宠物 120×120 DIP（未 Show 的测试窗口
/// HWND=0 → DPI 1.0，DIP=物理像素），目标可见外框 (700,420,1000,670)，
/// 脚点 (660,620) 距外框 40px（≤200 允许起攀）。伪造句柄的落位/遮挡
/// 校验经测试缝替换（只换采样来源，落位/提交/事件路径不变）。
/// </summary>
[TestClass]
public class ClimbEntryTests
{
    private const double PetLeft = 600;
    private const double PetTop = 500;
    private const double PetWidth = 120;
    private const double PetHeight = 120;

    // 与 WindowClimbAppTests 一致的目标可见外框（物理像素）。
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
                "ClimbEntryTests: MainWindow construction failed: {0}", ex);
            return null;
        }
    }

    private static void SetField(object obj, string name, object value)
        => GetFieldInfo(obj.GetType(), name)!.SetValue(obj, value);

    private static FieldInfo? GetFieldInfo(Type type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f is not null) return f;
        }
        return null;
    }

    /// <summary>反射驱动一轮拾取轮询（同步测试不泵消息，确定性执行）。</summary>
    private static void InvokeUpdatePerchPick(MainWindow window, double nowMs)
        => typeof(MainWindow).GetMethod("UpdatePerchPick",
               BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { nowMs });

    /// <summary>反射进入菜单模式（开菜单打断拾取的取消点）。</summary>
    private static void InvokeEnterContextMenuMode(MainWindow window)
        => typeof(MainWindow).GetMethod("EnterContextMenuMode",
               BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);

    /// <summary>泵 Dispatcher 到 Background 优先级，让 BeginInvoke 排队的
    /// 菜单命令（Normal 优先级）先执行完。</summary>
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>合格的近距外部窗口快照（起攀距离满足、落位采样用测试缝替换）。</summary>
    private static ExternalWindowSnapshot FakeSnapshot() => new(
        0x99999, "climb-entry-target", "FakeClass", 5000, "fakeproc",
        true, true, false, false, false, TLeft, TTop, TRight, TBottom);

    /// <summary>统一布景：宠物位置 + 攀爬/栖息测试缝（强制落位可见）。</summary>
    private static PetWindowClimb Setup(MainWindow window, List<PetClimbEvent> events)
    {
        window.Left = PetLeft;
        window.Top = PetTop;
        // 环境无关布景：注入单一 100% 监视器，使测试窗口 HWND 的 DPI 查询
        // （未显示窗口固定返回 1.0）与显示器信息一致。否则在高 DPI 屏
        // （如 150%）上，菜单路径 EndDrag→ClampWindowToWorkArea 经
        // WindowPositionAdapter 前向（source DPI=1.0）/反向（target DPI=1.5）
        // 换算把宠物错位搬移（实测 (600,500)→(380,293.33)，起攀距离
        // 40→260px 致 rejected-too-far），测试布景被环境破坏。
        window.TestMonitorList = new List<MonitorInfo>
        {
            new("CLIMB-MON",
                new MonitorRectPx(0, 0, 1920, 1080),
                new WorkAreaPx(new MonitorRectPx(0, 0, 1920, 1040)),
                1.0, 1.0)
        };
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
    public void ClimbPick_MenuEntry_StartsPickThenClickStartsClimb()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double clock = 0;
            window.TestGetElapsedMs = () => clock;
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);

            // 1) 正式菜单入口进入攀爬拾取模式（原生菜单同一分发路径）。
            window.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdClimbOnWindow);
            PumpDispatcher();

            Assert.IsTrue(window.IsPerchPickActive, "菜单命令后必须进入拾取模式。");
            Assert.IsTrue(window.IsClimbPickActive, "攀爬菜单必须进入攀爬意图。");
            Assert.AreEqual("active", window.LastPerchPickStatus);

            // 2) 边沿触发：先释放进入时的残键，再按下点击目标。
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 400);
            Assert.IsTrue(window.IsPerchPickActive, "释放帧不得结束拾取。");

            window.TestPerchPickInput = () => (true, 750, 430); // 目标内部、宠物矩形外
            window.TestPerchPickSnapshotSource = (_, _) => FakeSnapshot();
            clock = 500;
            InvokeUpdatePerchPick(window, clock);

            Assert.AreEqual("started", window.LastPerchPickStatus,
                "点击合格近距目标必须经正式入口起攀。");
            Assert.IsFalse(window.IsPerchPickActive, "起攀后必须退出拾取模式。");
            Assert.IsFalse(window.IsClimbPickActive);
            Assert.IsTrue(climb.IsActive, "起攀后攀爬控制器必须激活。");
            Assert.AreEqual(0x99999L, climb.TargetHwndValue);
            Assert.AreEqual("climb-started", events[0].Type);
            AssertRestPosition(window);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ClimbPick_LeftClickOnPetSelf_CancelsWithoutClimbing()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            window.StartClimbPick();
            Assert.IsTrue(window.IsClimbPickActive);

            // 边沿触发：先喂一帧释放。
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 50);
            // 点击点取宠物自身矩形中心（不选自己、不起攀）。
            var petRect = (PetRectPx)typeof(MainWindow)
                .GetMethod("GetPetRectPhysicalPx", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, null)!;
            var cx = (int)(petRect.Left + petRect.Width / 2);
            var cy = (int)(petRect.Top + petRect.Height / 2);
            window.TestPerchPickInput = () => (true, cx, cy);
            window.TestPerchPickSnapshotSource = (_, _) => FakeSnapshot();

            InvokeUpdatePerchPick(window, 100);

            Assert.AreEqual("cancelled-self", window.LastPerchPickStatus);
            Assert.IsFalse(window.IsPerchPickActive, "点中自身后必须退出拾取。");
            Assert.IsFalse(climb.IsActive, "点中自身不得起攀。");
            Assert.AreEqual(0, events.Count, "点自身不得发攀爬事件。");
            AssertRestPosition(window);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ClimbPick_IneligibleTarget_RejectedAndPickEnds()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            window.StartClimbPick();

            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 50);
            // 点中最小化的外部窗口：不合格。
            window.TestPerchPickInput = () => (true, 750, 430);
            window.TestPerchPickSnapshotSource = (_, _) => new ExternalWindowSnapshot(
                0x77777, "minimized-target", "FakeClass", 5000, "fakeproc",
                true, true, true, false, false, TLeft, TTop, TRight, TBottom);

            InvokeUpdatePerchPick(window, 100);

            Assert.AreEqual("rejected-ineligible", window.LastPerchPickStatus);
            Assert.IsFalse(window.IsPerchPickActive, "拒绝后必须结束拾取。");
            Assert.IsFalse(climb.IsActive, "不合格目标不得起攀。");
            AssertRestPosition(window);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ClimbPick_TooFarTarget_RejectedWithoutMoving()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            window.StartClimbPick();

            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 50);
            // 目标外框距脚点 740px（>200）：起攀距离拒绝。
            window.TestPerchPickInput = () => (true, 1600, 450);
            window.TestPerchPickSnapshotSource = (_, _) => new ExternalWindowSnapshot(
                0x88888, "far-target", "FakeClass", 5000, "fakeproc",
                true, true, false, false, false, 1400, 420, 1700, 670);

            InvokeUpdatePerchPick(window, 100);

            Assert.AreEqual("rejected-too-far", window.LastPerchPickStatus);
            Assert.IsFalse(window.IsPerchPickActive, "拒绝后必须结束拾取。");
            Assert.IsFalse(climb.IsActive, "超距目标不得起攀。");
            Assert.AreEqual(0, events.Count, "拒绝不得发攀爬事件。");
            AssertRestPosition(window);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ClimbPick_LandingOccluded_RejectedWithoutMoving()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            // 顶部落位脚点被完全遮挡。
            climb.TestLandingOcclusionSampler = (_, _, _) => (0.0, new List<string> { "occluder" });
            window.StartClimbPick();

            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 50);
            window.TestPerchPickInput = () => (true, 750, 430);
            window.TestPerchPickSnapshotSource = (_, _) => FakeSnapshot();

            InvokeUpdatePerchPick(window, 100);

            Assert.AreEqual("rejected-occluded", window.LastPerchPickStatus);
            Assert.IsFalse(window.IsPerchPickActive, "拒绝后必须结束拾取。");
            Assert.IsFalse(climb.IsActive, "遮挡目标不得起攀。");
            Assert.AreEqual(0, events.Count, "拒绝不得发攀爬事件。");
            AssertRestPosition(window);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ClimbPick_Timeout_Cancels()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            window.TestPerchPickTimeoutMs = 1000;
            window.StartClimbPick();
            window.TestPerchPickInput = () => (false, 0, 0); // 未按下：等待中

            InvokeUpdatePerchPick(window, 500);
            Assert.IsTrue(window.IsClimbPickActive, "超时前拾取必须保持。");

            InvokeUpdatePerchPick(window, 1500);
            Assert.AreEqual("cancelled-timeout", window.LastPerchPickStatus);
            Assert.IsFalse(window.IsPerchPickActive, "超时后必须退出拾取。");
            Assert.IsFalse(window.IsClimbPickActive);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ClimbPick_EntryWhileBusy_DoesNotEnterPickMode()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            var events = new List<PetClimbEvent>();
            Setup(window, events);
            SetField(window, "_isPhysicsActive", true);
            window.StartClimbPick();

            Assert.AreEqual("rejected-busy", window.LastPerchPickStatus,
                "位置被占用时不得进入攀爬拾取。");
            Assert.IsFalse(window.IsPerchPickActive);
            Assert.IsFalse(window.IsClimbPickActive);
            AssertRestPosition(window);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ClimbPick_OpeningMenu_CancelsPick()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            var events = new List<PetClimbEvent>();
            Setup(window, events);
            window.StartClimbPick();
            Assert.IsTrue(window.IsClimbPickActive);

            InvokeEnterContextMenuMode(window);

            Assert.AreEqual("cancelled", window.LastPerchPickStatus);
            Assert.IsFalse(window.IsPerchPickActive, "开菜单必须取消攀爬拾取。");
            Assert.IsFalse(window.IsClimbPickActive);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ClimbPick_PauseDuringPick_CancelsImmediately_ResumeDoesNotResume()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double clock = 0;
            window.TestGetElapsedMs = () => clock;
            var events = new List<PetClimbEvent>();
            Setup(window, events);
            window.StartClimbPick();
            window.TestPerchPickInput = () => (true, 750, 430); // 按住左键处于边沿触发等待段
            InvokeUpdatePerchPick(window, 100);
            Assert.IsTrue(window.IsClimbPickActive, "前置：暂停前攀爬拾取必须仍激活。");

            window.PauseAnimation();

            Assert.AreEqual("cancelled", window.LastPerchPickStatus,
                "暂停入口必须同步结束攀爬拾取。");
            Assert.IsFalse(window.IsPerchPickActive);
            Assert.IsFalse(window.IsClimbPickActive, "暂停必须清除边沿触发等待标记。");
            Assert.IsFalse(window.IsPerchPickAwaitingRelease);

            // 恢复：不续接旧拾取。
            clock = 5000;
            window.ResumeAnimation();
            window.TestPerchPickInput = () => (true, 750, 430);
            InvokeUpdatePerchPick(window, 5100);
            Assert.IsFalse(window.IsPerchPickActive, "恢复后不得续接暂停前的旧拾取。");
            Assert.IsFalse(window.IsClimbPickActive);
            Assert.IsFalse(window.Climb.IsActive, "恢复后旧拾取不得起攀。");
            AssertRestPosition(window);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ClimbPick_WhileClimbAlreadyActive_RejectedAndPickEnds()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = FakeSnapshot();
            climb.TestSnapshotSource = _ => snapshot;
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            // 攀爬进行中：位置被攀爬持有 → 不应进入攀爬拾取。
            window.StartClimbPick();

            Assert.AreEqual("rejected-busy", window.LastPerchPickStatus,
                "攀爬进行中不得进入攀爬拾取（位置被攀爬持有）。");
            Assert.IsFalse(window.IsPerchPickActive);
            Assert.IsFalse(window.IsClimbPickActive);
            Assert.IsTrue(climb.IsActive, "既有攀爬路线不得被打断。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchPick_ParallelEntry_RemainsUnchanged()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double clock = 0;
            window.TestGetElapsedMs = () => clock;
            var events = new List<PetClimbEvent>();
            Setup(window, events);

            // 平行入口回归：Perch on Window 仍进入栖息拾取并选中落位。
            window.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdPerchOnWindow);
            PumpDispatcher();

            Assert.IsTrue(window.IsPerchPickActive, "菜单命令后必须进入拾取模式。");
            Assert.IsFalse(window.IsClimbPickActive, "栖息入口不得进入攀爬意图。");
            Assert.AreEqual("active", window.LastPerchPickStatus);

            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 400);
            window.TestPerchPickInput = () => (true, 750, 430);
            window.TestPerchPickSnapshotSource = (_, _) => FakeSnapshot();
            clock = 500;
            InvokeUpdatePerchPick(window, clock);

            Assert.AreEqual("selected", window.LastPerchPickStatus,
                "平行栖息入口行为必须不变。");
            Assert.IsTrue(window.Perch.IsActive, "栖息入口必须选中落位。");
            Assert.IsFalse(window.Climb.IsActive, "栖息入口不得触发攀爬。");
            Assert.AreEqual(0, events.Count, "栖息入口不得发攀爬事件。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ClimbPick_FullChain_ClimbRouteDrivesToPerchHandoff()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double clock = 0;
            window.TestGetElapsedMs = () => clock;
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);
            var snapshot = FakeSnapshot();

            // 正式入口 → 点击合格近距目标 → 起攀。
            window.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdClimbOnWindow);
            PumpDispatcher();
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 400);
            window.TestPerchPickInput = () => (true, 750, 430);
            window.TestPerchPickSnapshotSource = (_, _) => snapshot;
            clock = 500;
            InvokeUpdatePerchPick(window, clock);

            Assert.AreEqual("started", window.LastPerchPickStatus);
            Assert.IsTrue(climb.IsActive);

            // 既有路线推进（重复观测同一目标快照）：大步到达 → 交接栖息。
            climb.TestSnapshotSource = _ => snapshot;
            climb.TestSpeedPxPerMs = 50.0;
            climb.Tick(700);

            Assert.IsFalse(climb.IsActive, "到达后必须结束路线。");
            Assert.IsTrue(window.Perch.IsActive, "到达后必须交接栖息。");
            Assert.AreEqual(0x99999L, window.Perch.TargetHwndValue);
            var last = events[^1];
            Assert.AreEqual("climb-completed", last.Type, "完整关键链必须完成交接。");
            // 落位脚点 = 可见外框上缘中点：(850, 420)；窗口左上 = (790, 300)。
            Assert.AreEqual(850 - PetWidth / 2, window.Left, 0.001, "落位必须无可见跳动。");
            Assert.AreEqual(420 - PetHeight, window.Top, 0.001);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void RecoverPet_DuringClimbPick_EndsPickAndOldClickDoesNotClimb()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double clock = 0;
            window.TestGetElapsedMs = () => clock;
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);

            // 生产入口：正式菜单进入攀爬拾取（尚未选择目标）。
            window.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdClimbOnWindow);
            PumpDispatcher();
            Assert.IsTrue(window.IsClimbPickActive, "前置：找回前攀爬拾取必须仍激活。");

            // 托盘找回：App.OnRecoverRequested 直接遍历调用 RecoverPet，
            // 不经 EnterContextMenuMode，没有任何隐式取消可用。
            window.RecoverPet();

            Assert.AreEqual("cancelled", window.LastPerchPickStatus,
                "找回必须同步结束尚未选目标的攀爬拾取。");
            Assert.IsFalse(window.IsPerchPickActive, "找回后不得残留拾取模式。");
            Assert.IsFalse(window.IsClimbPickActive, "找回后不得残留攀爬意图。");
            Assert.IsFalse(window.IsPerchPickAwaitingRelease, "找回后必须清除边沿触发等待标记。");

            // 找回后旧点击：拾取已结束，不得重新起攀、不得移动宠物。
            var leftAfterRecover = window.Left;
            var topAfterRecover = window.Top;
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 600);
            window.TestPerchPickInput = () => (true, 750, 430);
            window.TestPerchPickSnapshotSource = (_, _) => FakeSnapshot();
            clock = 700;
            InvokeUpdatePerchPick(window, clock);

            Assert.IsFalse(window.IsPerchPickActive, "找回后旧点击不得重新进入拾取。");
            Assert.IsFalse(climb.IsActive, "找回后旧点击不得起攀。");
            Assert.AreEqual(0, events.Count, "找回后旧点击不得发攀爬事件。");
            Assert.AreEqual(leftAfterRecover, window.Left, 0.001, "找回后旧点击不得移动宠物。");
            Assert.AreEqual(topAfterRecover, window.Top, 0.001, "找回后旧点击不得移动宠物。");

            // 推进 Tick：不得启动旧攀爬路线或再次移动宠物。
            climb.TestSnapshotSource = _ => FakeSnapshot();
            climb.Tick(1000);
            Assert.IsFalse(climb.IsActive, "找回后推进 Tick 不得启动旧路线。");
            Assert.AreEqual(leftAfterRecover, window.Left, 0.001, "找回后推进 Tick 不得移动宠物。");
            Assert.AreEqual(topAfterRecover, window.Top, 0.001, "找回后推进 Tick 不得移动宠物。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchPick_RecoverDuringPick_EndsSelectionNoParallelRegression()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            var events = new List<PetClimbEvent>();
            Setup(window, events);

            // 共享拾取状态机：栖息意图同样在待选阶段。
            window.StartPerchPick();
            Assert.IsTrue(window.IsPerchPickActive, "前置：栖息拾取必须仍激活。");
            Assert.IsFalse(window.IsClimbPickActive, "栖息入口不得进入攀爬意图。");

            window.RecoverPet();

            Assert.AreEqual("cancelled", window.LastPerchPickStatus,
                "找回必须同步结束栖息拾取（共享状态机语义一致，无平行入口回归）。");
            Assert.IsFalse(window.IsPerchPickActive, "找回后不得残留栖息拾取。");
            Assert.IsFalse(window.IsClimbPickActive);

            // 找回后旧点击：不得重新落位/起攀/移动宠物。
            var leftAfterRecover = window.Left;
            var topAfterRecover = window.Top;
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 100);
            window.TestPerchPickInput = () => (true, 750, 430);
            window.TestPerchPickSnapshotSource = (_, _) => FakeSnapshot();
            InvokeUpdatePerchPick(window, 200);

            Assert.IsFalse(window.IsPerchPickActive, "找回后旧点击不得重新进入栖息拾取。");
            Assert.IsFalse(window.Perch.IsActive, "找回后旧点击不得重新落位。");
            Assert.IsFalse(window.Climb.IsActive, "找回后旧点击不得触发攀爬。");
            Assert.AreEqual(0, events.Count, "找回后不得发攀爬事件。");
            Assert.AreEqual(leftAfterRecover, window.Left, 0.001, "找回后旧点击不得移动宠物。");
            Assert.AreEqual(topAfterRecover, window.Top, 0.001, "找回后旧点击不得移动宠物。");
        }
        finally { window.Close(); }
    }

    // ── R12 F-07：夜间 sleep 底态下成功起攀必须让位（正式入口 + 生产 Tick）──

    /// <summary>R12 F-07 布景：实例假时钟固定夜间 00:30（23:30 ≤ 00:30 &lt;
    /// 06:00）、光标远离宠物（生产 tick 的 hover 检测经测试缝隔离，防随机
    /// 光标触发好奇破坏布景）。</summary>
    private static void ApplyNightly(MainWindow window)
    {
        window.TestGetLocalNow = () => new DateTime(2026, 1, 15, 0, 30, 0);
        window.TestGetCursorPos = () => (9999, 9999);
    }

    /// <summary>
    /// R12 F-07：夜间 sleep 空闲底态下，成功起攀（正式菜单入口 + 点击合格
    /// 目标）必须让位，实际播放 side→edge→land 三阶段；路线/呈现期间生产
    /// tick 不得被夜间重判覆盖回 sleep；land 自然播完回 idle 后，下一生产
    /// tick 按夜间合法重入 sleep（驱动收尾口径：正常完成后夜间回 sleep 合法）。
    /// </summary>
    [STATestMethod]
    public void NightClimb_SleepYieldsToStartedClimb_PlaysStages_ReturnsToSleepAfterLand()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double clock = 0;
            window.TestGetElapsedMs = () => clock;
            ApplyNightly(window);
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);

            // 生产 tick：夜间 idle → sleep 空闲底态。
            window.OnUpdateTickTest();
            Assert.AreEqual("sleep", window.CurrentAction, "夜间生产 tick 必须进入 sleep 底态。");
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "sleep 不得显示镜像。");

            // 正式菜单入口进入攀爬拾取：不得唤醒 sleep（R12：不建议 StartPick 唤醒）。
            window.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdClimbOnWindow);
            PumpDispatcher();
            Assert.IsTrue(window.IsClimbPickActive, "夜间 sleep 中必须可进入攀爬拾取。");
            Assert.AreEqual("sleep", window.CurrentAction, "进入拾取不得唤醒 sleep。");

            // 边沿触发：释放残键 → 点击合格近距目标（目标在右 → 镜像翻转）。
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 400);
            var target = FakeSnapshot();
            window.TestPerchPickInput = () => (true, 750, 430);
            window.TestPerchPickSnapshotSource = (_, _) => target;
            clock = 500;
            InvokeUpdatePerchPick(window, clock);

            Assert.AreEqual("started", window.LastPerchPickStatus, "点击合格目标必须起攀。");
            Assert.IsTrue(climb.IsActive, "起攀后路线必须激活。");
            Assert.AreEqual("climb_side", window.CurrentAction,
                "成功起攀后 sleep 必须让位，实际播放 climb_side。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "目标在右：镜像翻转。");

            // 路线由生产 Tick 推进（与 sleep 接线同帧协同），持续观察到场。
            climb.TestSnapshotSource = _ => target;
            clock = 700;
            window.OnUpdateTickTest(); // dt=200 → 90px → (700,570)，仍侧边段
            clock = 1100;
            window.OnUpdateTickTest(); // dt=200 → 90px → (700,480)，仍侧边段
            Assert.IsTrue(climb.IsActive, "生产 tick 推进中路线必须保持激活。");
            Assert.AreEqual("climb_side", window.CurrentAction,
                "side 段生产 tick 不得被夜间重判覆盖回 sleep。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "side 段镜像保持。");

            clock = 1300;
            window.OnUpdateTickTest(); // dt=200 → 90px，贴到上缘角落 (730,420)；此帧仍 side 段
            Assert.IsTrue(climb.IsActive, "生产 tick 推进中路线必须保持激活。");
            Assert.AreEqual("climb_side", window.CurrentAction,
                "贴角帧仍处 side 段，不得被夜间重判覆盖回 sleep。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "side 段镜像保持。");

            clock = 1500;
            window.OnUpdateTickTest(); // dt=200 → 90px，沿上缘水平行进 (820,420) → 上缘段
            Assert.IsTrue(climb.IsActive, "上缘段路线必须保持激活。");
            Assert.AreEqual("climb_edge", window.CurrentAction, "上缘段必须实际播放 climb_edge。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "edge 段镜像保持目标朝向。");

            // 到达（大速度单帧直达，与既有到达测试同模式）：播 climb_land 并交接栖息。
            climb.TestSpeedPxPerMs = 50.0;
            clock = 1700;
            window.OnUpdateTickTest();
            Assert.IsFalse(climb.IsActive, "到达后必须结束路线。");
            Assert.IsTrue(window.Perch.IsActive, "到达后必须交接栖息。");
            Assert.AreEqual("climb_land", window.CurrentAction, "到达必须实际播放 climb_land。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "land 播放期间保留朝向。");
            Assert.AreEqual("climb-started", events[0].Type, "事件流必须以 climb-started 开头。");
            Assert.IsTrue(events.Exists(e => e.Type == "climb-arrived"), "事件流必须含 climb-arrived。");
            Assert.IsTrue(events.Exists(e => e.Type == "climb-completed"), "事件流必须以交接完成收尾。");

            // land once 总时长 3000ms（AnimationPlayer 用真实 MonotonicClock）：
            // 播完自动回 idle，下一生产 tick 按夜间合法重入 sleep（R12 收尾口径）。
            Thread.Sleep(3100);
            window.OnUpdateTickTest(); // 完成判定 → BeginInvoke 回 idle
            PumpDispatcher();
            window.OnUpdateTickTest(); // idle → 夜间重判
            PumpDispatcher();

            Assert.IsFalse(climb.IsActive, "完成后路线不得复活。");
            Assert.AreEqual("sleep", window.CurrentAction,
                "正常完成后夜间回 sleep 为合法空闲底态（R12 口径）。");
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "完成后镜像必须复位。");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// R12 F-07：夜间 sleep 中经正式入口点击不合格目标（超距）→ 拒绝路径
    /// 不得起攀、不得唤醒 sleep、不得改镜像（R12：拒绝/取消不应造成无关唤醒）。
    /// </summary>
    [STATestMethod]
    public void NightClimb_RejectedTarget_KeepsSleep_NoWakeNoMirror()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            ApplyNightly(window);
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);

            window.OnUpdateTickTest();
            Assert.AreEqual("sleep", window.CurrentAction, "前置：夜间生产 tick 进入 sleep。");

            // 正式入口进入拾取（不唤醒），点击超距目标：拒绝路径。
            window.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdClimbOnWindow);
            PumpDispatcher();
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 400);
            window.TestPerchPickInput = () => (true, 1600, 450);
            window.TestPerchPickSnapshotSource = (_, _) => new ExternalWindowSnapshot(
                0x88880, "far-target", "FakeClass", 5000, "fakeproc",
                true, true, false, false, false, 1400, 420, 1700, 670);

            InvokeUpdatePerchPick(window, 500);

            Assert.AreEqual("rejected-too-far", window.LastPerchPickStatus, "超距必须拒绝。");
            Assert.IsFalse(window.IsPerchPickActive, "拒绝后必须退出拾取。");
            Assert.IsFalse(climb.IsActive, "拒绝不得起攀。");
            Assert.AreEqual("sleep", window.CurrentAction,
                "拒绝路径不得唤醒 sleep（R12：拒绝不应造成无关唤醒）。");
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "拒绝不得改镜像。");
            Assert.AreEqual(0, events.Count, "拒绝不得发攀爬事件。");
            AssertRestPosition(window);

            // 拒绝后生产 tick：继续保持 sleep 空闲底态。
            window.OnUpdateTickTest();
            Assert.AreEqual("sleep", window.CurrentAction, "拒绝后生产 tick 仍保持 sleep。");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// R12 F-07：夜间成功起攀后正常取消（拖动打断）→ 即时回 idle 并复位
    /// 镜像；下一生产 tick 按夜间空闲底态回到 sleep（取消后回 sleep 合法，
    /// 攀爬过程中不能一直 sleep 的要求针对的是路线/呈现期间）。
    /// </summary>
    [STATestMethod]
    public void NightClimb_CancelAfterStart_ReturnsToSleepOnNextProductionTick()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double clock = 0;
            window.TestGetElapsedMs = () => clock;
            ApplyNightly(window);
            var events = new List<PetClimbEvent>();
            var climb = Setup(window, events);

            window.OnUpdateTickTest();
            Assert.AreEqual("sleep", window.CurrentAction, "前置：夜间进入 sleep 底态。");

            // 正式入口起攀成功：sleep 让位 climb_side。
            window.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdClimbOnWindow);
            PumpDispatcher();
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 400);
            var target = FakeSnapshot();
            window.TestPerchPickInput = () => (true, 750, 430);
            window.TestPerchPickSnapshotSource = (_, _) => target;
            clock = 500;
            InvokeUpdatePerchPick(window, clock);

            Assert.AreEqual("started", window.LastPerchPickStatus, "点击合格目标必须起攀。");
            Assert.AreEqual("climb_side", window.CurrentAction, "起攀后 sleep 必须让位。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "目标在右：镜像翻转。");

            // 正常取消（拖动打断）：立即结束路线回 idle 并复位镜像。
            climb.CancelForDrag();
            Assert.IsFalse(climb.IsActive, "取消后路线必须结束。");
            Assert.AreEqual("idle", window.CurrentAction, "取消即时回 idle（不直接跳 sleep）。");
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "取消必须复位镜像。");

            // 生产 tick：夜间空闲底态重判回 sleep。
            clock = 600;
            window.OnUpdateTickTest();
            Assert.AreEqual("sleep", window.CurrentAction,
                "取消后夜间生产 tick 必须回 sleep 空闲底态（R12 口径）。");
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "回 sleep 无镜像残留。");
        }
        finally { window.Close(); }
    }
}