using DesktopPet.Core.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>P5B-001（R-08）：窗口有限攀爬路线纯逻辑单元测试。</summary>
[TestClass]
public class WindowClimbTests
{
    private const uint SelfPid = 400;

    private static ExternalWindowSnapshot Snapshot(
        long hwnd = 0x10101,
        int left = 100, int top = 200, int right = 500, int bottom = 600) => new(
        hwnd, "无标题 - 记事本", "Notepad", 500, "notepad",
        true, true, false, false, false, left, top, right, bottom);

    // ---------- 起攀距离 ----------

    [TestMethod]
    public void Distance_OutsideFrame_IsEuclideanDistance()
    {
        var target = Snapshot(); // (100,200)-(500,600)
        // 脚点在外框左下角外侧 (50, 700)：dx=50, dy=100 → √(2500+10000)。
        Assert.AreEqual(Math.Sqrt(12500.0),
            WindowClimbPolicy.DistanceToFramePx(target, 50, 700), 1e-9);
    }

    [TestMethod]
    public void Distance_InsideFrame_IsZero()
    {
        var target = Snapshot();
        Assert.AreEqual(0.0, WindowClimbPolicy.DistanceToFramePx(target, 300, 400));
        // 恰在边框上也是 0。
        Assert.AreEqual(0.0, WindowClimbPolicy.DistanceToFramePx(target, 100, 200));
        Assert.AreEqual(0.0, WindowClimbPolicy.DistanceToFramePx(target, 500, 600));
    }

    [TestMethod]
    public void Distance_NullOrDegenerate_IsInfinity()
    {
        Assert.AreEqual(double.PositiveInfinity,
            WindowClimbPolicy.DistanceToFramePx(null, 100, 100));
        Assert.AreEqual(double.PositiveInfinity,
            WindowClimbPolicy.DistanceToFramePx(Snapshot(right: 100), 100, 100));
        Assert.AreEqual(double.PositiveInfinity,
            WindowClimbPolicy.DistanceToFramePx(Snapshot(bottom: 200), 100, 100));
    }

    [TestMethod]
    public void StartDistance_ThresholdInclusive()
    {
        var target = Snapshot();
        // 脚点 (300, 800)：恰好在框下方 200px → 距离=200，阈值默认 200 允许。
        Assert.IsTrue(WindowClimbPolicy.IsStartDistanceSatisfied(target, 300, 800));
        // 201px → 超过默认阈值拒绝。
        Assert.IsFalse(WindowClimbPolicy.IsStartDistanceSatisfied(target, 300, 801));
        // 自定义阈值。
        Assert.IsTrue(WindowClimbPolicy.IsStartDistanceSatisfied(target, 300, 900, maxStartDistancePx: 300));
        Assert.IsFalse(WindowClimbPolicy.IsStartDistanceSatisfied(target, 300, 900, maxStartDistancePx: 100));
    }

    // ---------- 路线构建 ----------

    [TestMethod]
    public void Route_NearLeftSide_GoesUpLeftEdgeToTopLanding()
    {
        var target = Snapshot(); // 上缘中点 (300, 200)
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var lx, out var ly));

        // 脚点贴近左侧边 (60, 500)：距左边 40、距右边 440 → 走左边。
        var (w1x, w1y, w2x, w2y) = WindowClimbPolicy.BuildRoute(target, 60, 500, lx, ly);
        Assert.AreEqual(100.0, w1x);
        Assert.AreEqual(500.0, w1y, "进入点 y=脚点 y（贴住侧边后垂直上行）。");
        Assert.AreEqual(100.0, w2x);
        Assert.AreEqual(200.0, w2y, "上沿点 y=外框上缘。");
    }

    [TestMethod]
    public void Route_NearRightSide_GoesUpRightEdgeToTopLanding()
    {
        var target = Snapshot();
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var lx, out var ly));

        // 脚点贴近右侧边 (560, 450) → 走右边。
        var (w1x, w1y, w2x, w2y) = WindowClimbPolicy.BuildRoute(target, 560, 450, lx, ly);
        Assert.AreEqual(500.0, w1x);
        Assert.AreEqual(450.0, w1y);
        Assert.AreEqual(500.0, w2x);
        Assert.AreEqual(200.0, w2y);
    }

    [TestMethod]
    public void Route_FootAboveFrameTop_EnterClampedToTop()
    {
        var target = Snapshot();
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var lx, out var ly));

        // 脚点高于外框上缘：进入点 y 钳到 BoundsTop，不越出外框。
        var (w1x, w1y, _, _) = WindowClimbPolicy.BuildRoute(target, 60, 100, lx, ly);
        Assert.AreEqual(100.0, w1x);
        Assert.AreEqual(200.0, w1y);
    }

    // ---------- 上缘阶段重建回归（M-17 复现：上缘卡死） ----------

    [TestMethod]
    public void Route_OnTopEdgeInsideSpan_GoesHorizontalStraightToLanding()
    {
        // 实机 M-17 场景：目标 bounds=[295,160,1085,655]，上缘中点 x=690；
        // 脚点已爬上上缘但停在上缘左侧 (313,160)。
        // 修复前：每 tick 重建路线把脚点拉回最近侧边（左缘 295）再折返，净位移抵消卡死；
        // 修复后：直接沿上缘水平走向落位脚点，不再绕回侧边。
        var target = Snapshot(left: 295, top: 160, right: 1085, bottom: 655);
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var lx, out var ly));
        Assert.AreEqual(690.0, lx, 1e-9);
        Assert.AreEqual(160.0, ly, 1e-9);

        var (w1x, w1y, w2x, w2y) = WindowClimbPolicy.BuildRoute(target, 313, 160, lx, ly);
        Assert.AreEqual(313.0, w1x);
        Assert.AreEqual(160.0, w1y);
        Assert.AreEqual(313.0, w2x);
        Assert.AreEqual(160.0, w2y);
    }

    [TestMethod]
    public void Route_OnTopEdgeOutsideSpan_StillTurnsToNearestSide()
    {
        // 脚点高于上缘但水平在跨度外（如 280 < 左缘 295）：不走水平分支，
        // 仍按“最近侧边→上缘”折线进入，保证跨度外脚点不被直接拉去落位。
        var target = Snapshot(left: 295, top: 160, right: 1085, bottom: 655);
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var lx, out var ly));

        var (w1x, w1y, w2x, w2y) = WindowClimbPolicy.BuildRoute(target, 280, 160, lx, ly);
        Assert.AreEqual(295.0, w1x);
        Assert.AreEqual(160.0, w1y);
        Assert.AreEqual(295.0, w2x);
        Assert.AreEqual(160.0, w2y);
    }

    [TestMethod]
    public void Route_RebuiltEachTickOnTopEdge_AdvancesMonotonicallyToLanding()
    {
        // 模拟调度循环：每 tick 用当前脚点重建路线并推进 5px。
        // 修复前：脚点 313 → 拉回左缘 295 → 折返，x 震荡不前进（M-17 卡 0.29）；
        // 修复后：x 单调不减，最终收敛到上缘中点 690。
        var target = Snapshot(left: 295, top: 160, right: 1085, bottom: 655);
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var lx, out var ly));

        double x = 313.0, y = 160.0;
        var prev = x;
        for (var tick = 0; tick < 300; tick++)
        {
            var (w1x, w1y, w2x, w2y) = WindowClimbPolicy.BuildRoute(target, x, y, lx, ly);
            (x, y) = WindowClimbPolicy.AdvanceAlongRoute(x, y, w1x, w1y, w2x, w2y, lx, ly, 5);
            Assert.IsTrue(x >= prev - 1e-9, $"tick {tick}: 上缘推进 x 应单调不减（x={x:R}, prev={prev:R}）");
            prev = x;
            if (Math.Abs(x - lx) <= 1e-9 && Math.Abs(y - ly) <= 1e-9)
                break;
        }

        Assert.AreEqual(lx, x, 1e-9);
        Assert.AreEqual(ly, y, 1e-9);
    }

    // ---------- 沿线推进 ----------

    [TestMethod]
    public void Advance_FirstSegment_InterpolatesLinearly()
    {
        var target = Snapshot();
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var lx, out var ly));
        var (w1x, w1y, w2x, w2y) = WindowClimbPolicy.BuildRoute(target, 60, 500, lx, ly);

        // 第一段 (60,500)→(100,500) 长 40：前进 20 → 中点 (80, 500)。
        var (x, y) = WindowClimbPolicy.AdvanceAlongRoute(60, 500, w1x, w1y, w2x, w2y, lx, ly, 20);
        Assert.AreEqual(80.0, x, 1e-9);
        Assert.AreEqual(500.0, y, 1e-9);
    }

    [TestMethod]
    public void Advance_AcrossSegments_ClimbsThenWalksTopEdge()
    {
        var target = Snapshot();
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var lx, out var ly));
        var (w1x, w1y, w2x, w2y) = WindowClimbPolicy.BuildRoute(target, 60, 500, lx, ly);

        // 第一段 40 + 第二段 300（垂直上行 500→200）= 340；再走 100 → 上缘 x=200。
        var (x, y) = WindowClimbPolicy.AdvanceAlongRoute(60, 500, w1x, w1y, w2x, w2y, lx, ly, 440);
        Assert.AreEqual(200.0, x, 1e-9);
        Assert.AreEqual(200.0, y, 1e-9);
    }

    [TestMethod]
    public void Advance_BeyondTotalLength_ConvergesAtLanding()
    {
        var target = Snapshot();
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var lx, out var ly));
        var (w1x, w1y, w2x, w2y) = WindowClimbPolicy.BuildRoute(target, 60, 500, lx, ly);

        // 任意超大推进量都收敛到落位脚点，不越界。
        var (x, y) = WindowClimbPolicy.AdvanceAlongRoute(60, 500, w1x, w1y, w2x, w2y, lx, ly, 1e9);
        Assert.AreEqual(lx, x, 1e-9);
        Assert.AreEqual(ly, y, 1e-9);
    }

    [TestMethod]
    public void Advance_ZeroOrNegative_StaysAtCurrentFoot()
    {
        var target = Snapshot();
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var lx, out var ly));
        var (w1x, w1y, w2x, w2y) = WindowClimbPolicy.BuildRoute(target, 60, 500, lx, ly);

        var (x0, y0) = WindowClimbPolicy.AdvanceAlongRoute(60, 500, w1x, w1y, w2x, w2y, lx, ly, 0);
        Assert.AreEqual(60.0, x0);
        Assert.AreEqual(500.0, y0);
        var (xn, yn) = WindowClimbPolicy.AdvanceAlongRoute(60, 500, w1x, w1y, w2x, w2y, lx, ly, -5);
        Assert.AreEqual(60.0, xn);
        Assert.AreEqual(500.0, yn);
    }

    [TestMethod]
    public void Advance_ExactLandingFootAlreadyAtLanding_NoMovement()
    {
        // 已在落位脚点：折线长度为 0，任何推进量都停在落位脚点。
        var target = Snapshot();
        Assert.IsTrue(WindowPerchLandingPolicy.TryComputeLanding(target, out var lx, out var ly));
        var (w1x, w1y, w2x, w2y) = WindowClimbPolicy.BuildRoute(target, lx, ly, lx, ly);
        var (x, y) = WindowClimbPolicy.AdvanceAlongRoute(lx, ly, w1x, w1y, w2x, w2y, lx, ly, 100);
        Assert.AreEqual(lx, x, 1e-9);
        Assert.AreEqual(ly, y, 1e-9);
    }
}
