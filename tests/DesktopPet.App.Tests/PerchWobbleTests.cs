using System.Reflection;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P5A-002：栖息跟随晃动反馈的 App 级测试。
///
/// 覆盖（用户验收口径：移动时晃、停下回稳、打断后无残留）：
/// - 跟随成功（follow Landed）→ 晃动启动（角度非零、纯视觉不改窗口位置）；
/// - 停止移动（无新 follow）→ 衰减包络在期限内自动回稳（角度归零）；
/// - 拖动接管（CancelForDrag）/暂停（PauseAnimation）/遮挡脱离 →
///   晃动立即结束且角度归零（无画面残留）。
///
/// 伪造句柄的落脚区 WindowFromPoint 永远命中其他根窗口，无法在单测
/// 构造"未遮挡"的 Landed 跟随；用 TestLandingOcclusionSampler 测试缝
/// 只替换遮挡采样来源，OnTick 的 Observe→gate→TryApplyLanding→提交
/// 全走生产路径。
/// </summary>
[TestClass]
public class PerchWobbleTests
{
    private static MainWindow? TryCreateMainWindow()
    {
        try
        {
            return new MainWindow();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "PerchWobbleTests: MainWindow construction failed: {0}", ex);
            return null;
        }
    }

    private static object? GetField(object obj, string name)
        => GetFieldInfo(obj.GetType(), name)?.GetValue(obj);

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

    private static void InvokeOnTick(PetWindowPerch perch)
        => typeof(PetWindowPerch).GetMethod("OnTick",
               BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(perch, null);

    /// <summary>合格外部目标快照（绑定基线，外框 100,200,900,700）。</summary>
    private static ExternalWindowSnapshot FakeSnapshot() => new(
        0x99999, "fake-perch-target", "FakeClass", 5000, "fakeproc",
        true, true, false, false, false, 100, 200, 900, 700);

    /// <summary>目标移动后的新外框（身份不变，仅 bounds 变化）。</summary>
    private static ExternalWindowSnapshot MovedSnapshot() => new(
        0x99999, "fake-perch-target", "FakeClass", 5000, "fakeproc",
        true, true, false, false, false, 100, 800, 900, 1500);

    /// <summary>
    /// 反射激活栖息（伪造合格目标），并注入"移动后快照+未遮挡采样"，
    /// 使生产轮询 OnTick 产生一次成功跟随（follow Landed）。
    /// 返回 perch。
    /// </summary>
    private static PetWindowPerch ActivatePerchForFollow(MainWindow window)
    {
        var perch = window.Perch;
        var tracker = (PerchTargetTracker)GetField(perch, "_tracker")!;
        Assert.IsTrue(tracker.Select(FakeSnapshot(), (uint)Environment.ProcessId),
            "测试前提：目标必须可被跟踪。");
        typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, true);
        ((PerchUpdateGate)GetField(perch, "_updateGate")!).Reset(100, 200, 900, 700);
        perch.TestSnapshotSource = _ => MovedSnapshot();
        perch.TestLandingOcclusionSampler = (_, _, _) => (1.0, new List<string>());
        return perch;
    }

    [STATestMethod]
    public void PerchFollowLanded_WobbleStarts_ThenSettlesWhenTargetStops()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double now = 5000;
            window.TestGetElapsedMs = () => now;
            var perch = ActivatePerchForFollow(window);
            var events = new List<PetPerchEvent>();
            perch.PerchEvent += events.Add;

            // 移动时晃：生产轮询跟随成功 → 晃动启动。
            var topBefore = window.Top;
            InvokeOnTick(perch);
            Assert.AreEqual("follow", events.LastOrDefault(e => e.Type == "follow")?.Type,
                "测试前提：必须产生一次成功跟随。");
            Assert.AreNotEqual(topBefore, window.Top, 0.001, "跟随必须实际移动窗口（生产行为不变）。");
            Assert.IsTrue(window.IsPerchWobbling, "跟随成功后必须开始晃动。");
            // 跟随本身可以移动窗口（已验收落位行为）；晃动必须是纯视觉——
            // 后续晃动 tick 不得再改窗口位置。
            var leftAfterFollow = window.Left;
            var topAfterFollow = window.Top;

            // 晃动画面随 tick 推进（角度非零，绕脚底中心）。
            now += 50;
            window.OnUpdateTickTest();
            Assert.IsTrue(Math.Abs(window.PerchWobbleAngleDeg) > 0.5,
                $"晃动期间角度必须非零（实际 {window.PerchWobbleAngleDeg:F2}°）。");
            Assert.AreEqual(leftAfterFollow, window.Left, 0.001,
                "晃动是纯视觉，不得改窗口水平位置。");
            Assert.AreEqual(topAfterFollow, window.Top, 0.001,
                "晃动是纯视觉，不得改窗口垂直位置。");
            Assert.AreEqual(110.0, window.WobbleTransform.CenterY, 0.001,
                "R1-1：旋转中心必须是帧偏移补偿后的素材脚点（footAnchorY，脚底贴住目标上沿）。");
            Assert.AreEqual(60.0, window.WobbleTransform.CenterX, 0.001);

            // 停下回稳：无新 follow，衰减包络在期限内自动归零。
            now += 3000;
            window.OnUpdateTickTest();
            Assert.IsFalse(window.IsPerchWobbling, "停止移动后晃动必须自动回稳结束。");
            Assert.AreEqual(0.0, window.PerchWobbleAngleDeg, 0.001, "回稳后角度必须为零（无残留）。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchWobble_DragPreempt_EndsImmediately_NoResidue()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double now = 5000;
            window.TestGetElapsedMs = () => now;
            var perch = ActivatePerchForFollow(window);
            InvokeOnTick(perch);
            Assert.IsTrue(window.IsPerchWobbling, "测试前提：晃动已启动。");

            // 晃到非零角度后拖动接管。
            now += 50;
            window.OnUpdateTickTest();
            Assert.IsTrue(Math.Abs(window.PerchWobbleAngleDeg) > 0.5, "测试前提：角度非零。");

            perch.CancelForDrag();

            Assert.IsFalse(window.IsPerchWobbling, "拖动接管必须立即结束晃动。");
            Assert.AreEqual(0.0, window.PerchWobbleAngleDeg, 0.001, "打断后角度必须归零（无残留）。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchWobble_Pause_EndsImmediately_NoResidue()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double now = 5000;
            window.TestGetElapsedMs = () => now;
            var perch = ActivatePerchForFollow(window);
            InvokeOnTick(perch);
            now += 50;
            window.OnUpdateTickTest();
            Assert.IsTrue(window.IsPerchWobbling, "测试前提：晃动已启动。");

            window.PauseAnimation();

            Assert.IsFalse(window.IsPerchWobbling, "暂停必须立即结束晃动。");
            Assert.AreEqual(0.0, window.PerchWobbleAngleDeg, 0.001, "打断后角度必须归零（无残留）。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchWobble_OccludedDetach_EndsImmediately_NoResidue()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double now = 5000;
            window.TestGetElapsedMs = () => now;
            var perch = ActivatePerchForFollow(window);
            InvokeOnTick(perch);
            now += 50;
            window.OnUpdateTickTest();
            Assert.IsTrue(window.IsPerchWobbling, "测试前提：晃动已启动。");

            // 目标随后移动到被遮挡位置：脱离（occluded-detach）必须立即结束晃动。
            perch.TestLandingOcclusionSampler = (_, _, _) => (0.0, new List<string> { "cover" });
            InvokeOnTick(perch);

            Assert.IsFalse(perch.IsActive, "遮挡脱离必须解除绑定。");
            Assert.IsFalse(window.IsPerchWobbling, "脱离必须立即结束晃动。");
            Assert.AreEqual(0.0, window.PerchWobbleAngleDeg, 0.001, "打断后角度必须归零（无残留）。");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// R1-1：非零晃动角度时，素材脚点（帧偏移补偿后的 footAnchor）
    /// 和窗口位置都保持不变——旋转绕素材脚点进行，脚底始终贴住目标上沿。
    /// </summary>
    [STATestMethod]
    public void PerchWobble_NonZeroAngle_MaterialFootAndWindowStayFixed()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double now = 5000;
            window.TestGetElapsedMs = () => now;
            var perch = ActivatePerchForFollow(window);
            InvokeOnTick(perch);
            Assert.IsTrue(window.IsPerchWobbling, "测试前提：晃动已启动。");

            // 推进到非零角度。
            now += 65;
            window.OnUpdateTickTest();
            Assert.IsTrue(Math.Abs(window.PerchWobbleAngleDeg) > 0.5,
                $"测试前提：角度非零（实际 {window.PerchWobbleAngleDeg:F2}°）。");

            // 帧偏移补偿后的素材脚点（Image 本地渲染坐标）。
            var character = window.TestCharacter;
            var footLocal = new System.Windows.Point(
                character.FootAnchorX + window.FrameOffsetTransform.X,
                character.FootAnchorY + window.FrameOffsetTransform.Y);

            // 旋转中心必须等于素材脚点（绕它旋转）。
            Assert.AreEqual(footLocal.X, window.WobbleTransform.CenterX, 0.001,
                "R1-1：旋转中心必须是帧偏移补偿后的素材脚点。");
            Assert.AreEqual(footLocal.Y, window.WobbleTransform.CenterY, 0.001);

            // 非零角度下，整个变换组（帧偏移 + 旋转）作用于素材脚点必须保持
            // 该点不动——实际素材脚点位置不变（脚底贴住目标上沿）。
            var group = window.PetImage.RenderTransform as System.Windows.Media.TransformGroup;
            Assert.IsNotNull(group, "测试前提：PetImage.RenderTransform 必须是 TransformGroup。");
            var footAfter = group.Transform(footLocal);
            Assert.AreEqual(footLocal.X, footAfter.X, 0.001,
                $"R1-1：非零角度（{window.PerchWobbleAngleDeg:F2}°）下素材脚点 X 必须不动。");
            Assert.AreEqual(footLocal.Y, footAfter.Y, 0.001,
                $"R1-1：非零角度（{window.PerchWobbleAngleDeg:F2}°）下素材脚点 Y 必须不动。");

            // 窗口位置也不得因晃动改变（纯视觉）。
            now += 50;
            var leftBefore = window.Left;
            var topBefore = window.Top;
            window.OnUpdateTickTest();
            Assert.IsTrue(Math.Abs(window.PerchWobbleAngleDeg) > 0.5, "测试前提：角度仍非零。");
            Assert.AreEqual(leftBefore, window.Left, 0.001, "晃动不得改窗口水平位置。");
            Assert.AreEqual(topBefore, window.Top, 0.001, "晃动不得改窗口垂直位置。");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// R1-2：连续跟随期间保留晃动相位（每次 follow 不重置起始时刻）——
    /// 持续移动时角度有正有负（不反复只向一侧摆），停止后归零。
    /// </summary>
    [STATestMethod]
    public void PerchWobble_ContinuousFollow_KeepsPhase_SwingsBothWays_ThenSettles()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double now = 5000;
            window.TestGetElapsedMs = () => now;
            var perch = ActivatePerchForFollow(window);
            var events = new List<PetPerchEvent>();
            perch.PerchEvent += events.Add;

            // 第一次跟随：启动晃动。
            InvokeOnTick(perch);
            Assert.AreEqual("follow", events.Last(e => e.Type == "follow").Type, "测试前提：follow #1。");
            Assert.IsTrue(window.IsPerchWobbling, "测试前提：晃动已启动。");

            // t=65ms（四分之一周期）：正向峰值附近。
            now += 65;
            window.OnUpdateTickTest();
            var anglePos = window.PerchWobbleAngleDeg;
            Assert.IsTrue(anglePos > 0.5, $"四分之一周期处角度必须为正（实际 {anglePos:F2}°）。");
            var startMs = (double)GetField(window, "_perchWobbleStartMs")!;

            // 目标继续移动：第二次跟随到来时不得重置相位（R1-2）。
            perch.TestSnapshotSource = _ => new ExternalWindowSnapshot(
                0x99999, "fake-perch-target", "FakeClass", 5000, "fakeproc",
                true, true, false, false, false, 100, 1400, 900, 2100);
            InvokeOnTick(perch);
            Assert.AreEqual("follow", events.Last(e => e.Type == "follow").Type, "测试前提：follow #2（目标继续移动）。");
            Assert.IsTrue(window.IsPerchWobbling, "连续跟随时晃动必须继续。");
            Assert.AreEqual(startMs, (double)GetField(window, "_perchWobbleStartMs")!, 0.001,
                "R1-2：连续跟随期间不得重置晃动起始时刻（保留相位）。");

            // t=195ms（四分之三周期）：反方向峰值附近——持续移动时角度有正有负。
            now += 130;
            window.OnUpdateTickTest();
            var angleNeg = window.PerchWobbleAngleDeg;
            Assert.IsTrue(angleNeg < -0.5, $"四分之三周期处角度必须为负（实际 {angleNeg:F2}°）。");
            Assert.AreEqual(startMs, (double)GetField(window, "_perchWobbleStartMs")!, 0.001,
                "R1-2：振荡期间起始时刻保持不变。");

            // 停下：无新 follow，衰减包络内自动归零。
            now += 3000;
            window.OnUpdateTickTest();
            Assert.IsFalse(window.IsPerchWobbling, "停止移动后晃动必须自动回稳结束。");
            Assert.AreEqual(0.0, window.PerchWobbleAngleDeg, 0.001, "回稳后角度必须为零（无残留）。");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// R2 F-01：晃动中切换动作帧（idle→curious，offsetY 0→9）时，
    /// 旋转中心与帧偏移在同一次 UpdateFrame 更新——原始素材脚点
    /// 经完整变换（帧偏移+旋转）后，与只应用帧偏移时的位置一致。
    /// 反例场景：独立复现晃动中切 curious 帧，若中心未随帧偏移同步，
    /// 素材脚点会横移（约 1.17 DIP）。
    /// </summary>
    [STATestMethod]
    public void PerchWobble_FrameSwitchDuringWobble_MaterialFootStaysAtFrameOffsetOnlyPosition()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double now = 5000;
            window.TestGetElapsedMs = () => now;
            var perch = ActivatePerchForFollow(window);
            InvokeOnTick(perch);
            Assert.IsTrue(window.IsPerchWobbling, "测试前提：晃动已启动。");

            // 晃到非零角度（idle 帧，偏移 0）。
            now += 65;
            window.OnUpdateTickTest();
            Assert.IsTrue(window.PerchWobbleAngleDeg > 0.5,
                $"测试前提：角度非零（实际 {window.PerchWobbleAngleDeg:F2}°）。");
            Assert.AreEqual(0, window.FrameOffsetTransform.Y, 0.001, "测试前提：idle 帧偏移为 0。");

            // 晃动中切到 curious 帧（offsetY 0→非零），走生产 UpdateFrame。
            var animPlayer = (DesktopPet.Core.Animation.AnimationPlayer)GetField(window, "_animPlayer")!;
            var curiousFrames = window.TestCharacter.Actions["curious"].Frames;
            Assert.IsTrue(curiousFrames.Count > 0 && curiousFrames[0].OffsetY > 0,
                "测试前提：curious 首帧 offsetY 必须非零（帧偏移补偿素材）。");
            var curiousOffsetY = (double)curiousFrames[0].OffsetY;
            var curiousOffsetX = (double)curiousFrames[0].OffsetX;
            animPlayer.PlayAction("curious");
            typeof(MainWindow).GetMethod("UpdateFrame",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);

            Assert.AreEqual(curiousOffsetY, window.FrameOffsetTransform.Y, 0.001,
                "curious 帧偏移必须是 JSON offsetY。");
            Assert.AreEqual(110.0 + curiousOffsetY, window.WobbleTransform.CenterY, 0.001,
                "R2 F-01：晃动中切帧时旋转中心必须随新帧偏移同步（素材脚点=footAnchor+offset）。");
            Assert.AreEqual(60.0 + curiousOffsetX, window.WobbleTransform.CenterX, 0.001);
            Assert.IsTrue(window.PerchWobbleAngleDeg > 0.5,
                $"测试前提：切帧后角度仍非零（实际 {window.PerchWobbleAngleDeg:F2}°）。");

            // 原始素材脚点（PNG 内 footAnchor(60,110)）经完整变换
            // （帧偏移+旋转）后，必须与只应用帧偏移时的位置一致。
            var group = window.PetImage.RenderTransform as System.Windows.Media.TransformGroup;
            Assert.IsNotNull(group, "测试前提：PetImage.RenderTransform 必须是 TransformGroup。");
            var rawFoot = new System.Windows.Point(60.0, 110.0);
            var expected = new System.Windows.Point(60.0 + window.FrameOffsetTransform.X,
                110.0 + window.FrameOffsetTransform.Y);
            var actual = group.Transform(rawFoot);
            Assert.AreEqual(expected.X, actual.X, 0.001,
                $"R2 F-01：晃动中切帧后素材脚点 X 必须与只应用帧偏移一致（expected {expected.X:F2}，实际 {actual.X:F2}）。");
            Assert.AreEqual(expected.Y, actual.Y, 0.001,
                $"R2 F-01：晃动中切帧后素材脚点 Y 必须与只应用帧偏移一致（expected {expected.Y:F2}，实际 {actual.Y:F2}）。");
        }
        finally { window.Close(); }
    }
}
