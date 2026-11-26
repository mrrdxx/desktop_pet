using System.Reflection;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Animation;
using DesktopPet.Core.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Windows.Threading;

namespace DesktopPet.App.Tests;

/// <summary>
/// P5B-003 S2：攀爬动作表现接线专项测试。
///
/// 覆盖：
/// - 起攀播 climb_side，镜像朝向目标侧边（目标在右 → ScaleX=-1；在左 → ScaleX=1）；
/// - 上缘段切 climb_edge（方向沿用落位脚点判据，镜像方向随目标保持）；
/// - 到达播 climb_land，交栖息后 land 自然播完 → 自动回 idle 且 ScaleX 复位为 1
///   （OnActionCompleted 路径不经 EndClimbAction，防镜像残留）；
/// - 拖动/暂停/找回/退出/遮挡/目标失效各取消路径：立即回 idle、无镜像残留
///   （写拒 write-rejected 与拖动同属"让位"语义，收口 StopCore→EndClimbAction，
///   与 occluded/dead 路径共享同一无残留保证，测试缝无法天然构造写拒竞争）；
/// - 缺失攀爬动作回退 idle 不崩溃；
/// - 优先级守卫：非攀爬表达（好奇等）不被攀爬播放覆盖，EndClimbAction 不误清。
///
/// 布景与 WindowClimbAppTests 一致：宠物 600,500(120×120 DIP，HWND=0→DPI 1.0)，
/// 目标可见外框 (700,420,1000,670)，脚点 (660,620)，起攀距离 40≤200。
/// sideX=700&gt;footX=660 → 目标在右 → 期望 ScaleX=-1。
/// </summary>
[TestClass]
public class ClimbActionTests
{
    private const double PetLeft = 600;
    private const double PetTop = 500;
    private const double PetWidth = 120;
    private const double PetHeight = 120;

    private const int TLeft = 700;
    private const int TTop = 420;
    private const int TRight = 1000;
    private const int TBottom = 670;

    /// <summary>实例级共享单调时钟：Setup 的 TestGetElapsedMs 捕获本字段，
    /// 测试体内直接推进（如 hover dwell 与攀爬同步大步进）。</summary>
    private double _clockMs;

    private static MainWindow? TryCreateMainWindow()
    {
        try
        {
            return new MainWindow();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "ClimbActionTests: MainWindow construction failed: {0}", ex);
            return null;
        }
    }

    private static ExternalWindowSnapshot Snapshot(
        long hwnd, int left = TLeft, int top = TTop, int right = TRight,
        int bottom = TBottom, bool minimized = false)
        => new(hwnd, "climb-action-target", "FakeClass", 5000, "fakeproc",
            true, true, minimized, false, false, left, top, right, bottom);

    /// <summary>统一布景：宠物位置 + 测试缝 + 动画/鼠标时钟隔离 + 事件流。
    /// 时钟由实例字段 <see cref="_clockMs"/> 承载，测试体内赋新值即可推进。</summary>
    private (PetWindowClimb Climb, double ClockMs) Setup(
        MainWindow window, List<PetClimbEvent> events)
    {
        window.Left = PetLeft;
        window.Top = PetTop;
        _clockMs = 0.0;
        window.TestGetElapsedMs = () => _clockMs;
        // 鼠标远离宠物：防止 hover 分支触发 curious 盖掉攀爬动作。
        window.TestGetCursorPos = () => (9999, 9999);
        var climb = window.Climb;
        climb.TestLandingOcclusionSampler = (_, _, _) => (1.0, new List<string>());
        climb.ClimbEvent += events.Add;
        window.Perch.TestLandingOcclusionSampler = (_, _, _) => (1.0, new List<string>());
        return (climb, _clockMs);
    }

    /// <summary>泵 Dispatcher 到 Background 优先级，让 BeginInvoke 排队
    /// （OnActionCompleted 回 idle 的 Normal 优先级回调）先执行完。</summary>
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void AssertCleanIdle(MainWindow window, string context)
    {
        Assert.AreEqual("idle", window.CurrentAction, $"{context}：动作必须立即回 idle。");
        Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
            $"{context}：镜像必须复位（无攀爬残影）。");
    }

    /// <summary>反射取动画播放器（模拟高阶表达时直接经播放器切动作）。</summary>
    private static AnimationPlayer GetAnimPlayer(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_animPlayer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is null)
            Assert.Fail("_animPlayer field not found.");
        return (AnimationPlayer)field!.GetValue(window)!;
    }

    // ── 起攀：climb_side + 镜像朝向目标 ────────────────────────────────

    [STATestMethod]
    public void Start_PlaysSide_ScalesTowardNearestTargetSide()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var (climb, _) = Setup(window, events);

            // 目标在宠物右侧（sideX=700 > 脚点 660）→ ScaleX=-1（朝右伸爪）。
            var rightTarget = Snapshot(0xAAA01);
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(rightTarget, (uint)Environment.ProcessId, 0));
            Assert.AreEqual("climb_side", window.CurrentAction, "起攀必须播 climb_side。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "目标在右：必须镜像翻转（ScaleX=-1）。");
            climb.CancelForDrag();
            AssertCleanIdle(window, "右目标起攀后取消");

            // 目标在宠物左侧（sideX=600 < 脚点 660）→ ScaleX=1（朝左，素材默认朝向）。
            var leftTarget = Snapshot(0xAAA02, left: 200, top: 420, right: 600, bottom: 670);
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(leftTarget, (uint)Environment.ProcessId, 0));
            Assert.AreEqual("climb_side", window.CurrentAction, "再次起攀必须播 climb_side。");
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "目标在左：素材默认朝左，不得反转（ScaleX=1）。");
        }
        finally { window.Close(); }
    }

    // ── 上缘段：climb_edge 切换，镜像随目标保持 ────────────────────────

    [STATestMethod]
    public void SideToEdge_SwitchesAction_KeepsMirrorTowardTarget()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var (climb, _) = Setup(window, events);
            var snapshot = Snapshot(0xAAA03);
            climb.TestSnapshotSource = _ => snapshot;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            Assert.AreEqual("climb_side", window.CurrentAction);

            // 默认速度 0.45px/ms、单 tick 上限 200ms（0.45*200=90px）：
            // 折线 [660,620]→[700,620]→[700,420]→[850,420] 共 390px。
            // tick@200→(700,570)、tick@400→(700,480)（分派仍 side）；
            // tick@600 起始 (700,480) 尚未上缘（仍 side），结束 (730,420)；
            // tick@800 起始 (730,420) 满足 onTopEdge → 切 climb_edge。
            // 时间戳必须单调递增：TryStart(nowMs:0) 将 _lastTickMs 置 0，
            // 重复 Tick(200) 除首个外 dt=0 不推进（P5B-001 F-02 单调时间守卫）。
            climb.Tick(200);
            climb.Tick(400);
            climb.Tick(600);
            climb.Tick(800);

            Assert.IsTrue(climb.IsActive, "上缘段路线仍在进行。");
            Assert.AreEqual("climb_edge", window.CurrentAction,
                "进入上缘段必须切换播 climb_edge。");
            // 上缘段方向判据用落位脚点：landingX=850 > 当前脚点 → 目标在右。
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "上缘段向落位脚点方向移动，镜像保持目标朝向。");
        }
        finally { window.Close(); }
    }

    // ── 到达：climb_land 播完自动回 idle 且镜像复位（关键新发现）────────

    [STATestMethod]
    public void Arrival_PlaysLand_ThenCompletesToIdle_ResetsMirror()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var (climb, _) = Setup(window, events);
            var snapshot = Snapshot(0xAAA04);
            climb.TestSnapshotSource = _ => snapshot;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));

            // 大步到达：0.45 默认 → 直接走到达分支。
            climb.TestSpeedPxPerMs = 50.0;
            climb.Tick(100);

            Assert.IsFalse(climb.IsActive, "到达后路线必须结束。");
            Assert.IsTrue(window.Perch.IsActive, "到达后必须交接栖息（不打断 land）。");
            Assert.AreEqual("climb_land", window.CurrentAction,
                "到达分支必须播 climb_land（不应被 StopCore 立即切回 idle）。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "land 播放期间保持最后攀爬朝向。");

            // land once 总时长 260+340+2400=3000ms（AnimationPlayer 用真实
            // MonotonicClock）：真实等待后触发帧刷新，ActionCompleted → 自动回
            // idle 且经 OnActionCompleted 复位镜像（不经 EndClimbAction）。
            Thread.Sleep(3100);
            window.OnUpdateTickTest();
            PumpDispatcher();
            window.OnUpdateTickTest();
            PumpDispatcher();

            AssertCleanIdle(window, "land 播完自动回 idle");
        }
        finally { window.Close(); }
    }

    // ── 各取消路径：立即回 idle、无镜像残留 ─────────────────────────────

    [STATestMethod]
    public void DragCancel_ImmediateIdle_AndResetsMirror()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            climb.TryStart(Snapshot(0xAAA05), (uint)Environment.ProcessId, 0);
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "起攀后已镜像。");

            climb.CancelForDrag();

            Assert.IsFalse(climb.IsActive);
            AssertCleanIdle(window, "拖动取消");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PauseCancel_ImmediateIdle_AndResetsMirror()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            climb.TryStart(Snapshot(0xAAA06), (uint)Environment.ProcessId, 0);

            climb.CancelForPause();

            Assert.IsFalse(climb.IsActive);
            AssertCleanIdle(window, "暂停取消");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void RecoverCancel_ImmediateIdle_AndResetsMirror()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            climb.TryStart(Snapshot(0xAAA07), (uint)Environment.ProcessId, 0);

            climb.CancelForRecover();

            Assert.IsFalse(climb.IsActive);
            AssertCleanIdle(window, "找回取消");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ExitCancel_ImmediateIdle_ResetsMirror_AndRestoresPosition()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            var snapshot = Snapshot(0xAAA08);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TryStart(snapshot, (uint)Environment.ProcessId, 0);
            climb.Tick(200); // 推进一段：位置已离开爬行前点。

            climb.CancelForExit();

            Assert.IsFalse(climb.IsActive);
            AssertCleanIdle(window, "退出取消");
            Assert.AreEqual(PetLeft, window.Left, 0.001, "退出必须恢复爬行前位置。");
            Assert.AreEqual(PetTop, window.Top, 0.001, "退出必须恢复爬行前位置。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void OccludedCancel_ImmediateIdle_AndResetsMirror()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            var snapshot = Snapshot(0xAAA09);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TryStart(snapshot, (uint)Environment.ProcessId, 0);

            // 起攀后落点被遮挡 → tick 走遮挡取消路径。
            climb.TestLandingOcclusionSampler = (_, _, _) => (0.0, new List<string> { "occluder" });
            climb.Tick(100);

            Assert.IsFalse(climb.IsActive);
            AssertCleanIdle(window, "落点遮挡取消");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void DeadTargetCancel_ImmediateIdle_AndResetsMirror()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            var snapshot = Snapshot(0xAAA0A);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TryStart(snapshot, (uint)Environment.ProcessId, 0);

            // 目标句柄销毁（采集返回 null → DeadSnapshot）→ 目标失效取消。
            climb.TestSnapshotSource = _ => null;
            climb.Tick(100);

            Assert.IsFalse(climb.IsActive);
            AssertCleanIdle(window, "目标失效取消");
        }
        finally { window.Close(); }
    }

    // ── 缺失动作回退与优先级守卫 ────────────────────────────────────────

    [STATestMethod]
    public void MissingAction_FallsBackToIdle_NoCrash()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (_, _) = Setup(window, new List<PetClimbEvent>());

            // 攀爬动作缺失（未注册）：AnimationPlayer 回退 idle，不崩溃。
            window.PlayClimbAction("climb_missing", mirrorLeft: true);
            Assert.AreEqual("idle", window.CurrentAction,
                "缺失攀爬动作必须回退 idle，不得崩溃。");
            // 回退后镜像随播放调用被设置，但动作是 idle —— 无残留风险；
            // 更关键的是下一个合法攀爬/回 idle 都能正常复位（下方断言）。
            window.PlayClimbAction("climb_side", mirrorLeft: false);

            Assert.AreEqual("climb_side", window.CurrentAction, "合法攀爬动作正常播放。");
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "mirrorLeft=false（目标在左）不得反转。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ClimbDoesNotOverrideActiveExpression()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (_, _) = Setup(window, new List<PetClimbEvent>());
            var animPlayer = GetAnimPlayer(window);

            // 模拟更高优先级表达已持有动作（好奇等）。
            animPlayer.PlayAction("curious");
            Assert.AreEqual("curious", window.CurrentAction, "前置于好奇表达。");

            // 攀爬播放守卫：当前非 idle/非 climb_* 时不得覆盖。
            window.PlayClimbAction("climb_side", mirrorLeft: true);
            Assert.AreEqual("curious", window.CurrentAction,
                "攀爬动作不得覆盖更高优先级表达。");
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "未播放攀爬时镜像不得被设置。");

            // 攀爬结束清理守卫：非攀爬动作时不得误清其他表达。
            window.EndClimbAction();
            Assert.AreEqual("curious", window.CurrentAction,
                "EndClimbAction 不得误清非攀爬表达。");
        }
        finally { window.Close(); }
    }

    // ── R7 F-03：land 播放中的生产中断必须清理动作与镜像 ─────────────

    [STATestMethod]
    public void LandPlayback_PauseClearsActionAndMirror()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            var snapshot = Snapshot(0xAAA0B);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TryStart(snapshot, (uint)Environment.ProcessId, 0);
            climb.TestSpeedPxPerMs = 50.0;
            climb.Tick(100); // 到达：land 播放中，路线已结束。

            Assert.IsFalse(climb.IsActive, "到达后路线必须结束。");
            Assert.AreEqual("climb_land", window.CurrentAction, "到达分支必须播 climb_land。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "land 播放期间保留最后攀爬朝向。");

            // 生产入口：PauseAnimation → CancelForPause（R7 F-03 宽松条件：
            // 路线不活跃但呈现活动仍在时同样清理）→ idle + 镜像复位。
            window.PauseAnimation();
            Assert.AreEqual("idle", window.CurrentAction,
                "land 播放中暂停必须立即回 idle（不留攀爬残影）。");
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "land 播放中暂停必须复位镜像。");
        }
        finally { window.Close(); }
    }

    // ── R7 F-04：缺失动作不镜像 idle；临时表达释放后补播 ─────────────

    [STATestMethod]
    public void MissingAction_DoesNotMirrorIdle()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (_, _) = Setup(window, new List<PetClimbEvent>());

            // 生产路径：缺失动作必须判定播放失败，且不改动作、不改镜像。
            var played = window.PlayClimbAction("climb_missing", mirrorLeft: true);
            Assert.IsFalse(played, "缺失攀爬动作必须判定为播放失败。");
            Assert.AreEqual("idle", window.CurrentAction,
                "缺失动作保持 idle，不残留攀爬。");
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "缺失动作不得把 idle 帧镜像显示（ScaleX 必须为 1）。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ExpressionDuringClimb_ReplaysSideAfterRelease()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            var snapshot = Snapshot(0xAAA0C);
            climb.TestSnapshotSource = _ => snapshot;
            var animPlayer = GetAnimPlayer(window);

            // 临时高优先级表达先占用动作（如好奇）。
            animPlayer.PlayAction("curious");
            Assert.AreEqual("curious", window.CurrentAction, "前置于好奇表达。");

            // 起攀：位置所有权成立（Started），但 climb_side 播放被守卫
            // 拒绝，缓存不得记录"未播出"组合。
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            Assert.IsTrue(climb.IsActive, "表达占用只挡动作播放，不挡路线开始。");
            Assert.AreEqual("curious", window.CurrentAction, "起攀不得覆盖临时表达。");

            // 表达释放 → 下一 tick 经 DispatchStageAction 补播 climb_side。
            animPlayer.PlayAction("idle");
            climb.Tick(50);
            Assert.AreEqual("climb_side", window.CurrentAction,
                "表达释放后下一 tick 必须补播 climb_side。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "补播时镜像按已选侧边（目标在右）设置。");
        }
        finally { window.Close(); }
    }

    // ── R7 F-05：贴边沿墙直爬不翻转镜像（左右双侧） ──────────────────

    [STATestMethod]
    public void SameSideWall_ContactKeepsMirror_NoFlip()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            var snapshot = Snapshot(0xAAA0D);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TryStart(snapshot, (uint)Environment.ProcessId, 0);
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "起攀目标在右：ScaleX=-1。");

            climb.Tick(200); // →(700,570)：侧边段，脚点尚未贴到侧边轴。
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "靠近侧边保持镜像不翻转。");

            climb.Tick(400); // →(700,480)：脚点贴到侧边轴 x=700。
            Assert.IsTrue(climb.IsActive, "贴边仍是侧边段路线。");
            Assert.AreEqual("climb_side", window.CurrentAction, "贴边仍播 climb_side。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "贴边沿墙直爬不得翻转镜像（w1x 与脚点同轴不能翻）。");

            // tick@600 从 (700,480) 推进 90px：沿侧边升 60px 到上缘 (700,420)、
            // 再水平 30px 到 (730,420)；动作分派用推进前脚点 (700,480) 仍不满足
            // onTopEdge（480 > 420），故仍播 climb_side、镜像 -1 保持贴边不翻转。
            climb.Tick(600);
            Assert.AreEqual("climb_side", window.CurrentAction,
                "到达上缘高度后水平推进一格：分派用推进前脚点，仍在侧边段。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "贴边段向落位方向推进，镜像保持目标朝向（不因贴边翻转复位）。");

            // tick@800 起始脚点 (730,420) 满足 onTopEdge（y=420≤420、x∈[700,1000]）
            // → 切 climb_edge；方向按落位脚点 landingX=850 > 730 → 镜像保持 -1。
            climb.Tick(800);
            Assert.AreEqual("climb_edge", window.CurrentAction, "到达上缘段必须切 climb_edge。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "上缘段向落位脚点方向移动，镜像保持目标朝向（不因贴边翻转复位）。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void LeftSideWall_KeepsNoFlip_ThenEdgeFollowsLanding()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            // 目标在宠物左侧：可见外框(200,300,600,620)，脚点(660,620)，
            // 最近侧边 x=600 < 660 → 素材默认朝左，ScaleX=1 不翻转。
            var snapshot = Snapshot(0xAAA0E, left: 200, top: 300, right: 600, bottom: 620);
            climb.TestSnapshotSource = _ => snapshot;
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "目标在左：素材默认朝左不翻转。");

            climb.Tick(200); // →(600,590)：靠近左墙。
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "靠近左墙保持朝左。");

            climb.Tick(400); // →(600,500)：贴到侧边轴 x=600。
            Assert.AreEqual("climb_side", window.CurrentAction, "左墙贴边仍播 climb_side。");
            Assert.AreEqual(1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "左墙贴边不得翻转镜像。");
        }
        finally { window.Close(); }
    }

    // ── R7 F-04：hover 不覆盖攀爬表现（生产 OnUpdateTick 正反例） ────

    /// <summary>取当前渲染帧身体不透明区中心的屏幕物理像素点（hover 命中）。
    /// 宠物窗口在 (600,500)，PetImage 填满画布，局部坐标即窗口内坐标。</summary>
    private static (int x, int y) PetHoverScreenPoint(MainWindow window)
    {
        var bounds = PetImageGeometry.OpaqueBounds(window.PetImage);
        if (bounds.IsEmpty)
            Assert.Fail("PetImage 帧源未加载，无法定位身体点。");
        return ((int)(window.Left + bounds.X + bounds.Width / 2),
                (int)(window.Top + bounds.Y + bounds.Height / 2));
    }

    [STATestMethod]
    public void HoverDwell_DoesNotOverrideClimbPresentation()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            var snapshot = Snapshot(0xAAA0F);
            climb.TestSnapshotSource = _ => snapshot;
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            Assert.AreEqual("climb_side", window.CurrentAction, "起攀已播 climb_side。");

            // 先驱动一帧（clock=0，光标远离）让 PetImage.Source 就绪并取身体点。
            window.OnUpdateTickTest();
            var (px, py) = PetHoverScreenPoint(window);
            window.TestGetCursorPos = () => (px, py);

            // 单帧大步：hover dwell 1300ms≥1200ms 达到触发条件；同时攀爬
            // 推进受 stepMs 上限 200ms 限制（90px）仍处侧边段。
            _clockMs = 1300;
            window.OnUpdateTickTest();

            Assert.IsTrue(climb.IsActive, "单帧大步行进受限，路线仍在进行。");
            Assert.AreEqual("climb_side", window.CurrentAction,
                "攀爬表现中 hover 触发不得覆盖为好奇。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "攀爬表现中 hover 不得影响镜像。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void HoverDwell_TriggersCurious_WhenNotClimbing()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (_, _) = Setup(window, new List<PetClimbEvent>());

            // 驱动一帧加载源；首次 tick（clock=0）光标仍在远处。
            window.OnUpdateTickTest();
            var (px, py) = PetHoverScreenPoint(window);
            window.TestGetCursorPos = () => (px, py);

            // dwell 起点 100ms，1400ms 时达到 1200ms 触发阈值。
            _clockMs = 100;
            window.OnUpdateTickTest();
            _clockMs = 1400;
            window.OnUpdateTickTest();

            Assert.AreEqual("curious", window.CurrentAction,
                "非攀爬时相同的 hover 设置必须正常触发好奇（守卫不得误伤）。");
        }
        finally { window.Close(); }
    }

    // ── R8 F-03：栖息实际脱离（生产 Perch.OnTick 轮询）释放 land 落位表现 ──

    /// <summary>反射驱动生产栖息轮询（与监源码 R8 反例同构）：仅替换目标
    /// 快照来源（测试缝），其余 Perch.OnTick 生产路径（Observe 状态机、
    /// 脱离收口 DetachInternal）原样执行。</summary>
    private static void DrivePerchOnTick(PetWindowPerch perch)
    {
        var method = typeof(PetWindowPerch).GetMethod("OnTick",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method is null)
            Assert.Fail("PetWindowPerch.OnTick not found.");
        method!.Invoke(perch, null);
    }

    [STATestMethod]
    public void LandPlayback_TargetGoneViaPerchOnTick_ReleasesClimbPresentation()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            var snapshot = Snapshot(0xAAA10);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TestSpeedPxPerMs = 50.0;
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));

            climb.Tick(100); // 到达：land 播放中，路线已结束、已交接栖息。

            Assert.IsFalse(climb.IsActive, "到达后路线必须结束。");
            Assert.IsTrue(window.Perch.IsActive, "到达后必须交接栖息（不打断 land）。");
            Assert.AreEqual("climb_land", window.CurrentAction, "到达分支必须播 climb_land。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "land 播放期间保留最后攀爬朝向。");

            // 生产栖息轮询：目标句柄销毁（快照采集返回 null）→ Observe
            // Cleared → DetachInternal 收口 → 释放攀爬落位表现（R8 F-03 接线）。
            window.Perch.TestSnapshotSource = _ => null;
            DrivePerchOnTick(window.Perch);

            Assert.IsFalse(window.Perch.IsActive,
                "生产栖息轮询必须脱离失效目标。");
            AssertCleanIdle(window, "land 播放中目标失效（Perch.OnTick 生产轮询）");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void LandPlayback_TargetValidViaPerchOnTick_KeepsClimbPresentation()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            var snapshot = Snapshot(0xAAA11);
            climb.TestSnapshotSource = _ => snapshot;
            climb.TestSpeedPxPerMs = 50.0;
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));

            climb.Tick(100); // 到达：land 播放中，已交接栖息。

            Assert.AreEqual("climb_land", window.CurrentAction, "到达分支必须播 climb_land。");

            // 目标仍然有效（外框变化，同句柄同身份）→ OnTick 走生产 follow
            // 分支（测试缝遮挡采样判定不遮挡）→ Landed 跟随，不脱离。
            var moved = Snapshot(0xAAA11, left: 720, top: 420, right: 1020, bottom: 670);
            window.Perch.TestSnapshotSource = _ => moved;
            DrivePerchOnTick(window.Perch);

            Assert.IsTrue(window.Perch.IsActive,
                "目标仍然有效时生产轮询不得脱离栖息。");
            Assert.AreEqual("climb_land", window.CurrentAction,
                "目标有效时不得误清 land 播放（不触发攀爬落位释放）。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "land 播放期间镜像保持（未误清）。");
        }
        finally { window.Close(); }
    }

    // ── R8 F-04：缓存相等仍须核对真实播放；已播放→覆盖→释放须补播、同阶段不重启 ──

    /// <summary>反射读动画播放器私有动作起始时刻（仅作相对比较：未重启时
    /// 两次采样必须相等；不依赖其实时时钟绝对值）。</summary>
    private static double ReadActionStartMs(AnimationPlayer player)
    {
        var field = typeof(AnimationPlayer).GetField("_actionStartMs",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null)
            Assert.Fail("_actionStartMs field not found.");
        return (double)field!.GetValue(player)!;
    }

    [STATestMethod]
    public void StagePlayedThenOverridden_ReleaseReplaysStage()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            var snapshot = Snapshot(0xAAA12);
            climb.TestSnapshotSource = _ => snapshot;

            // 起攀成功：climb_side 已实际播出并写入缓存（R7 F-04 仅成功才缓存）。
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            Assert.AreEqual("climb_side", window.CurrentAction, "起攀已成功播 climb_side。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "目标在右：镜像翻转。");

            // 已成功播过后被高优先级临时表达覆盖，再释放回 idle。
            GetAnimPlayer(window).PlayAction("curious");
            GetAnimPlayer(window).PlayAction("idle");
            Assert.AreEqual("idle", window.CurrentAction, "覆盖释放后当前回 idle。");

            // 生产 Tick：缓存相等但真实播放已非请求阶段 → 修复后必须重试
            // PlayClimbAction 补播（修复前缓存相等提前 return 保持 idle）。
            climb.Tick(50);
            Assert.IsTrue(climb.IsActive, "路线仍在进行（动作补播不结束路线）。");
            Assert.AreEqual("climb_side", window.CurrentAction,
                "已成功播放→被临时表达覆盖→释放后，下一 tick 必须补播 climb_side。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "补播时镜像按已选侧边设置。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void SameStageTick_DoesNotRestartPlayback()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var (climb, _) = Setup(window, new List<PetClimbEvent>());
            var snapshot = Snapshot(0xAAA13);
            climb.TestSnapshotSource = _ => snapshot;

            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            Assert.AreEqual("climb_side", window.CurrentAction, "起攀已播 climb_side。");

            var player = GetAnimPlayer(window);
            var startA = ReadActionStartMs(player);

            // 正常同阶段多 tick：缓存相等且当前真实播放=请求阶段+镜像相符
            // → 每 tick 都应跳过重启（_actionStartMs 不被 PlayAction 重写）。
            climb.Tick(200); // 仍侧边段（0.45*200=90px → (700,570)）。
            var startB = ReadActionStartMs(player);
            Assert.IsTrue(climb.IsActive, "同阶段 tick 后路线仍在进行。");
            Assert.AreEqual("climb_side", window.CurrentAction, "同阶段保持 climb_side。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001,
                "同阶段镜像保持目标朝向。");
            Assert.AreEqual(startA, startB, 0.0,
                "正常同阶段 tick 不得重启播放（动作起始时刻不得变化）。");

            climb.Tick(400); // →(700,480)：仍侧边段，贴边不翻（R7 F-05）。
            var startC = ReadActionStartMs(player);
            Assert.AreEqual(startB, startC, 0.0,
                "再次同阶段 tick 仍不得重启播放。");
        }
        finally { window.Close(); }
    }

    // ── R12 F-07：夜间路线活跃期间不得被重判覆盖回 sleep ──────────────────

    /// <summary>
    /// R12 F-07：夜间路线活跃期间，即使呈现被更高优先级表达短暂覆盖、释放
    /// 回 idle（进入任何空闲底态窗口），生产 tick 的 sleep 接线也必须让位
    /// （IsClimbOwned 显式排除路线活跃期），由攀爬控制器下一 tick 补播
    /// climb_side——防夜间重判覆盖有效攀爬路线。
    /// </summary>
    [STATestMethod]
    public void NightTick_ActiveRoute_IdleRelease_IsNotReplacedBySleep()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetClimbEvent>();
            var (climb, _) = Setup(window, events);
            window.TestGetLocalNow = () => new DateTime(2026, 1, 15, 0, 30, 0);

            // 夜间生产 tick：进入 sleep 底态。
            window.OnUpdateTickTest();
            Assert.AreEqual("sleep", window.CurrentAction, "前置：夜间进入 sleep 底态。");

            // 成功起攀（TryStart 生产守卫路径）：sleep 让位。
            var snapshot = Snapshot(0xAAA20);
            climb.TestSnapshotSource = _ => snapshot;
            Assert.AreEqual(PetClimbStartResult.Started,
                climb.TryStart(snapshot, (uint)Environment.ProcessId, 0));
            Assert.AreEqual("climb_side", window.CurrentAction, "起攀后 sleep 必须让位。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "目标在右：镜像翻转。");

            // 更高优先级表达短暂覆盖 → 释放回 idle；路线仍活跃。
            GetAnimPlayer(window).PlayAction("curious");
            GetAnimPlayer(window).PlayAction("idle");
            Assert.AreEqual("idle", window.CurrentAction, "覆盖释放后回 idle。");

            // 生产 tick（夜间）：sleep 接线因路线活跃让出，控制器补播 climb_side。
            _clockMs = 100;
            window.OnUpdateTickTest();

            Assert.IsTrue(climb.IsActive, "有效路线不得被生产 tick 结束。");
            Assert.AreEqual("climb_side", window.CurrentAction,
                "夜间重判不得把有效攀爬覆盖回 sleep（必须补播 climb_side）。");
            Assert.AreEqual(-1.0, window.ClimbMirrorTransform.ScaleX, 0.0001, "补播镜像保持目标朝向。");
        }
        finally { window.Close(); }
    }
}