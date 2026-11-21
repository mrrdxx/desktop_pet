namespace DesktopPet.Core.Windows;

/// <summary>
/// P5B-001（R-08）：窗口有限攀爬路线（纯逻辑）。
/// 表演式路线：当前脚点 → 最近的可见外框侧边（左/右）→ 沿侧边上升到
/// 外框上缘 → 沿上缘爬到顶部落位脚点（与 <see cref="WindowPerchLandingPolicy"/>
/// 上缘中点一致，落位点无可见跳动）。
/// 纪律（ARCHITECTURE §4/§9）：
/// - 坐标为虚拟桌面物理像素（允许负值）；本类不做任何 Win32 调用或
///   DIP 换算——物理像素到宠物窗口 DIP 的换算在 App 边界完成；
/// - 不承诺识别真实标题栏语义、不沿真实边框连续寻路（R-08 升级项）；
/// - 路线只是脚点折线；目标每 tick 重观测，路线由调用方按最新快照重建。
/// </summary>
public static class WindowClimbPolicy
{
    /// <summary>默认爬行速度（物理像素/毫秒）。</summary>
    public const double DefaultSpeedPxPerMs = 0.45;

    /// <summary>单 tick 最大允许推进时间（毫秒）。超过后丢弃积压
    /// （ARCHITECTURE §5 有限补步：MaxSteps×FixedStep 之外不追回）。</summary>
    public const double MaxStepTimeMs = 200.0;

    /// <summary>默认起攀距离上限：脚点到目标可见外框的欧氏距离不超过此值才允许起攀。</summary>
    public const double DefaultMaxStartDistancePx = 200.0;

    /// <summary>
    /// P5B-001 R1 F-02：折线 [from → w1 → w2 → landing] 的总长度（物理像素）。
    /// 到达判定与推进必须基于真实剩余折线长度（远程直线距离可能短于折线，
    /// 用它判定会提前交接造成落位跳跃）。
    /// </summary>
    public static double PolylineLengthPx(
        double fromX, double fromY,
        double w1X, double w1Y, double w2X, double w2Y,
        double landingX, double landingY)
    {
        double Seg(double ax, double ay, double bx, double by)
        {
            var dx = bx - ax;
            var dy = by - ay;
            return Math.Sqrt(dx * dx + dy * dy);
        }
        return Seg(fromX, fromY, w1X, w1Y)
            + Seg(w1X, w1Y, w2X, w2Y)
            + Seg(w2X, w2Y, landingX, landingY);
    }

    /// <summary>
    /// 脚点到目标可见外框的欧氏距离（物理像素）。脚点在外框内返回 0；
    /// 目标为 null 或外框退化返回 double.PositiveInfinity。
    /// </summary>
    public static double DistanceToFramePx(ExternalWindowSnapshot? target, double footX, double footY)
    {
        if (target is null || target.BoundsRight <= target.BoundsLeft || target.BoundsBottom <= target.BoundsTop)
            return double.PositiveInfinity;
        var dx = Math.Max(target.BoundsLeft - footX, Math.Max(0.0, footX - target.BoundsRight));
        var dy = Math.Max(target.BoundsTop - footY, Math.Max(0.0, footY - target.BoundsBottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>起攀距离是否满足（脚点足够靠近目标可见外框）。</summary>
    public static bool IsStartDistanceSatisfied(
        ExternalWindowSnapshot? target, double footX, double footY,
        double maxStartDistancePx = DefaultMaxStartDistancePx)
        => DistanceToFramePx(target, footX, footY) <= maxStartDistancePx;

    /// <summary>
    /// 构建从当前脚点到顶部落位脚点的爬行折线：
    ///   [ (sideX, enterY) → (sideX, topY) → (landingX, topY) ]
    /// - sideX 取距脚点较近的一侧（BoundsLeft / BoundsRight）；
    /// - enterY = min(max(footY, BoundsTop), BoundsBottom)（先贴住侧边）；
    /// - topY = BoundsTop（外框上缘，落位脚点的 y 与之一致）。
    /// 调用方保证落位脚点已通过 <see cref="WindowPerchLandingPolicy"/> 校验。
    /// </summary>
    public static (double Waypoint1X, double Waypoint1Y, double Waypoint2X, double Waypoint2Y)
        BuildRoute(ExternalWindowSnapshot target, double footX, double footY,
            double landingX, double landingY)
    {
        var topY = target.BoundsTop;
        // 脚点已处于外框上缘高度且水平在外框跨度内：直接沿上缘水平走向
        // 落位脚点，不再绕回“最近侧边”。否则每 tick 用当前脚点重建路线时，
        // 侧边选择会把已上缘的脚点拉回侧边再折返，净位移被抵消而卡死
        // （实机 M-17 复现：foot≈313 距左缘 295 近，fraction 停 0.29 震荡）。
        var onTopEdge = footY <= topY
            && footX >= target.BoundsLeft && footX <= target.BoundsRight;
        if (onTopEdge)
            return (footX, topY, footX, topY);
        var sideX = Math.Abs(footX - target.BoundsLeft) <= Math.Abs(footX - target.BoundsRight)
            ? target.BoundsLeft
            : target.BoundsRight;
        var enterY = Math.Min(Math.Max(footY, target.BoundsTop), target.BoundsBottom);
        return (sideX, enterY, sideX, topY);
    }

    /// <summary>
    /// 沿折线 [from → w1 → w2 → landing] 前进 advancePx 后的脚点位置。
    /// advancePx 超过折线总长时返回落位脚点（终点收敛，不越界）。
    /// </summary>
    public static (double X, double Y) AdvanceAlongRoute(
        double fromX, double fromY,
        double w1X, double w1Y, double w2X, double w2Y,
        double landingX, double landingY,
        double advancePx)
    {
        if (advancePx <= 0)
            return (fromX, fromY);

        // 已在落位脚点：任何推进都停在落位点（调用方到达守卫的兜底；
        // 不沿侧边折线离开落位点）。
        if (Math.Abs(fromX - landingX) <= 1e-9 && Math.Abs(fromY - landingY) <= 1e-9)
            return (landingX, landingY);

        var remaining = advancePx;
        double[] xs = { fromX, w1X, w2X, landingX };
        double[] ys = { fromY, w1Y, w2Y, landingY };
        for (var i = 0; i < 3; i++)
        {
            var dx = xs[i + 1] - xs[i];
            var dy = ys[i + 1] - ys[i];
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= remaining)
            {
                remaining -= len;
                continue;
            }
            var t = remaining / len;
            return (xs[i] + dx * t, ys[i] + dy * t);
        }
        return (landingX, landingY);
    }
}
