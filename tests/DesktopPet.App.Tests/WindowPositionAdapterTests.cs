using DesktopPet.App.Platform;
using DesktopPet.Core.Geometry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// F-02 R2: App-level coordinate adaptation tests for WindowPositionAdapter.
/// Covers cross-monitor DPI consistency, layout shrinkage recovery, and
/// primary-monitor recovery with injectable synthetic monitor layouts.
/// </summary>
[TestClass]
public class WindowPositionAdapterTests
{
    // Standard pet window size in DIP (matches MainWindow placeholder).
    private const double WinW = 120;
    private const double WinH = 140;

    // ── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Create a monitor at the given virtual-desktop coordinates with the
    /// specified DPI scale. Work area equals full bounds (no taskbar).
    /// </summary>
    private static MonitorInfo MakeMonitor(
        string name, int left, int top, int width, int height,
        double dpiScale)
    {
        var bounds = new MonitorRectPx(left, top, width, height);
        return new MonitorInfo(name, bounds, new WorkAreaPx(bounds), dpiScale, dpiScale);
    }

    /// <summary>
    /// Create a monitor with a taskbar-reduced work area.
    /// </summary>
    private static MonitorInfo MakeMonitorWithTaskbar(
        string name, int left, int top, int width, int height,
        int taskbarHeight, double dpiScale)
    {
        var full = new MonitorRectPx(left, top, width, height);
        var work = new MonitorRectPx(left, top, width, height - taskbarHeight);
        return new MonitorInfo(name, full, new WorkAreaPx(work), dpiScale, dpiScale);
    }

    // ── ClampToWorkArea ──────────────────────────────────────

    [TestMethod]
    public void Clamp_SameMonitor_100pct_PositionUnchangedIfVisible()
    {
        // Single 100% monitor: 1920×1080, work area = full.
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 1920, 1080, 1.0)
        };

        // Window at DIP (100, 100) → foot center at (160, 240) physical px.
        // This is well inside the work area, so clamp should be a no-op.
        var (left, top) = WindowPositionAdapter.ClampToWorkArea(
            monitors, 100, 100, WinW, WinH, 1.0, 1.0);

        Assert.AreEqual(100, left, 0.01, "Left should not change when already visible");
        Assert.AreEqual(100, top, 0.01, "Top should not change when already visible");
    }

    [TestMethod]
    public void Clamp_SameMonitor_150pct_PositionUnchangedIfVisible()
    {
        // Single 150% monitor: 2560×1440 physical, DPI scale 1.5.
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 2560, 1440, 1.5)
        };

        // Window at DIP (100, 100) with source DPI 1.5.
        // foot = (100*1.5 + 120*1.5/2, 100*1.5 + 140*1.5) = (240, 360).
        // Work area is 0..2560 × 0..1440, window physical = 180×210.
        // minFootX = 90, maxFootX = 2470, minFootY = 210, maxFootY = 1440.
        // 240 and 360 are well inside → no clamp.
        var (left, top) = WindowPositionAdapter.ClampToWorkArea(
            monitors, 100, 100, WinW, WinH, 1.5, 1.5);

        Assert.AreEqual(100, left, 0.01, "Left should not change at 150% when visible");
        Assert.AreEqual(100, top, 0.01, "Top should not change at 150% when visible");
    }

    [TestMethod]
    public void Clamp_CrossMonitor_100to150_TargetDpiUsedForReverse()
    {
        // Two monitors: primary 100% (1920×1080) on left,
        // secondary 150% (2560×1440) on right starting at x=1920.
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 1920, 1080, 1.0),
            MakeMonitor("MON2", 1920, 0, 2560, 1440, 1.5)
        };

        // Place window near the right edge of MON1 so it overflows into MON2.
        // DIP Left=1850, Top=400, source DPI=1.0 (MON1).
        // foot = (1850 + 60, 400 + 140) = (1910, 540) physical px → on MON1.
        // Clamp: MON1 work area right=1920, window phys width=120.
        // maxFootX = 1920 - 60 = 1860. So clamped foot X = 1860.
        // Target monitor = MON1 (1860 < 1920), DPI=1.0.
        // Reverse: leftDip = (1860 - 60) / 1.0 = 1800.
        var (left, top) = WindowPositionAdapter.ClampToWorkArea(
            monitors, 1850, 400, WinW, WinH, 1.0, 1.0);

        Assert.AreEqual(1800, left, 0.01, "Left should be clamped to MON1 right edge minus half window");
        Assert.AreEqual(400, top, 0.01, "Top should not change (already visible vertically)");
    }

    [TestMethod]
    public void Clamp_CrossMonitor_150to100_TargetDpiUsedForReverse()
    {
        // Two monitors: primary 150% (2560×1440) on left,
        // secondary 100% (1920×1080) on right starting at x=2560.
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 2560, 1440, 1.5),
            MakeMonitor("MON2", 2560, 0, 1920, 1080, 1.0)
        };

        // Window at DIP (1700, 400), source DPI=1.5 (MON1).
        // foot = (1700*1.5 + 120*1.5/2, 400*1.5 + 140*1.5) = (2640, 810).
        // 2640 > 2560 → on MON2. MON2 work area: 2560..4480 × 0..1080.
        // Window phys on MON2: 120*1.0=120, 140*1.0=140.
        // minFootX = 2560 + 60 = 2620, maxFootX = 4480 - 60 = 4420.
        // 2640 is inside [2620, 4420] → no X clamp.
        // minFootY = 0 + 140 = 140, maxFootY = 1080. 810 is inside → no Y clamp.
        // Target = MON2, DPI=1.0.
        // Reverse: leftDip = (2640 - 60) / 1.0 = 2580. topDip = (810 - 140) / 1.0 = 670.
        var (left, top) = WindowPositionAdapter.ClampToWorkArea(
            monitors, 1700, 400, WinW, WinH, 1.5, 1.5);

        Assert.AreEqual(2580, left, 0.01, "Left should use MON2 (100%) DPI for reverse conversion");
        Assert.AreEqual(670, top, 0.01, "Top should use MON2 (100%) DPI for reverse conversion");
    }

    [TestMethod]
    public void Clamp_OffScreen_LeftEdge_ClampsToWorkArea()
    {
        // Single 100% monitor, window placed far left (negative virtual desktop).
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 1920, 1080, 1.0)
        };

        // DIP Left=-500, Top=400 → foot = (-440, 540).
        // minFootX = 60, so clamped X = 60. Target = MON1, DPI=1.0.
        // Reverse: leftDip = (60 - 60) / 1.0 = 0.
        var (left, top) = WindowPositionAdapter.ClampToWorkArea(
            monitors, -500, 400, WinW, WinH, 1.0, 1.0);

        Assert.AreEqual(0, left, 0.01, "Left should be clamped to 0 (work area left edge)");
        Assert.AreEqual(400, top, 0.01, "Top should not change");
    }

    // ── RecoverPosition (layout shrinkage) ───────────────────

    [TestMethod]
    public void Recover_LayoutShrinkage_OffScreenMapsToPrimary()
    {
        // Simulate: pet was on MON2 which got disconnected.
        // New layout has only MON1 (primary 100%).
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 1920, 1080, 1.0)
        };

        // Pet was at DIP (2000, 400) with source DPI 1.0 (old MON2).
        // foot = (2060, 540). MON2 is gone, savedDeviceName=null.
        // RecoverPosition maps to primary (MON1), clamps to its work area.
        // MON1 work area: 0..1920 × 0..1080, window phys = 120×140.
        // maxFootX = 1920 - 60 = 1860. 2060 > 1860 → clamped to 1860.
        // maxFootY = 1080. 540 < 1080 and > 140 → no Y clamp.
        // Target = MON1, DPI=1.0.
        // Reverse: leftDip = (1860 - 60) / 1.0 = 1800. topDip = (540 - 140) / 1.0 = 400.
        var (left, top) = WindowPositionAdapter.RecoverPosition(
            monitors, 2000, 400, WinW, WinH, 1.0, 1.0, null);

        Assert.AreEqual(1800, left, 0.01, "Off-screen pet should be recovered to primary work area right edge");
        Assert.AreEqual(400, top, 0.01, "Top should be preserved if within work area");
    }

    [TestMethod]
    public void Recover_LayoutShrinkage_WithSavedDeviceName_MapsToPrimary()
    {
        // Pet was on "MON2" which is now gone. Only MON1 remains.
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 1920, 1080, 1.0)
        };

        // savedDeviceName="MON2" not found → maps to primary.
        var (left, top) = WindowPositionAdapter.RecoverPosition(
            monitors, 2000, 500, WinW, WinH, 1.0, 1.0, "MON2");

        // Should be on MON1, clamped. foot=(2060,640), maxFootX=1860, maxFootY=1080.
        // clamped to (1860, 640). Reverse with DPI 1.0: (1800, 500).
        Assert.AreEqual(1800, left, 0.01);
        Assert.AreEqual(500, top, 0.01);
    }

    [TestMethod]
    public void Recover_LayoutShrinkage_150pct_ToPrimary_100pct()
    {
        // Pet was on a 150% monitor (now gone), only 100% primary remains.
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 1920, 1080, 1.0)
        };

        // Source DPI=1.5 (old monitor). DIP Left=1000, Top=400.
        // foot = (1000*1.5 + 120*1.5/2, 400*1.5 + 140*1.5) = (1590, 810).
        // MON2 gone → primary MON1 (100%): work area 0..1920 × 0..1080.
        // Window phys on MON1: 120×140. minFootX=60, maxFootX=1860, minFootY=140, maxFootY=1080.
        // 1590 in [60,1860], 810 in [140,1080] → no clamp.
        // Target = MON1, DPI=1.0.
        // Reverse: leftDip = (1590-60)/1.0 = 1530. topDip = (810-140)/1.0 = 670.
        var (left, top) = WindowPositionAdapter.RecoverPosition(
            monitors, 1000, 400, WinW, WinH, 1.5, 1.5, null);

        Assert.AreEqual(1530, left, 0.01, "Cross-DPI recovery should use target (100%) DPI for reverse");
        Assert.AreEqual(670, top, 0.01, "Top should use target DPI for reverse conversion");
    }

    [TestMethod]
    public void Recover_SavedMonitorStillExists_ClampsToThatMonitor()
    {
        // Both monitors exist, pet saved on MON2.
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 1920, 1080, 1.0),
            MakeMonitor("MON2", 1920, 0, 2560, 1440, 1.5)
        };

        // Pet at DIP (3000, 400), source DPI=1.5 (MON2).
        // foot = (3000*1.5 + 120*1.5/2, 400*1.5 + 140*1.5) = (4590, 810).
        // savedDeviceName="MON2" found → clamp to MON2 work area.
        // MON2: 1920..4480 × 0..1440, phys window = 180×210.
        // minFootX = 1920 + 90 = 2010, maxFootX = 4480 - 90 = 4390.
        // 4590 > 4390 → clamped to 4390.
        // minFootY = 0 + 210 = 210, maxFootY = 1440. 810 in range.
        // Target = MON2, DPI=1.5.
        // Reverse: leftDip = (4390 - 90) / 1.5 = 2866.67. topDip = (810 - 210) / 1.5 = 400.
        var (left, top) = WindowPositionAdapter.RecoverPosition(
            monitors, 3000, 400, WinW, WinH, 1.5, 1.5, "MON2");

        Assert.AreEqual(2866.67, left, 0.1, "Should clamp to MON2 right edge with MON2 DPI");
        Assert.AreEqual(400, top, 0.1, "Top should use MON2 DPI for reverse");
    }

    // ── RecoverToPrimary ─────────────────────────────────────

    [TestMethod]
    public void RecoverToPrimary_SingleMonitor_CentersInWorkArea()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 1920, 1080, 1.0)
        };

        // Primary work area center: (960, 540).
        // Clamp: minFootX=60, maxFootX=1860, minFootY=140, maxFootY=1080.
        // 960 in [60,1860], 540 in [140,1080] → no clamp.
        // Reverse DPI 1.0: leftDip = (960-60)/1.0 = 900, topDip = (540-140)/1.0 = 400.
        var (left, top) = WindowPositionAdapter.RecoverToPrimary(monitors, WinW, WinH);

        Assert.AreEqual(900, left, 0.01, "Should center horizontally in primary work area");
        Assert.AreEqual(400, top, 0.01, "Should center vertically in primary work area");
    }

    [TestMethod]
    public void RecoverToPrimary_WithTaskbar_AvoidsTaskbarArea()
    {
        // 100% monitor with 40px taskbar at bottom.
        var monitors = new List<MonitorInfo>
        {
            MakeMonitorWithTaskbar("MON1", 0, 0, 1920, 1080, 40, 1.0)
        };

        // Work area: 0..1920 × 0..1040. Center: (960, 520).
        // Clamp: minFootY=140, maxFootY=1040. 520 in range.
        // Reverse: leftDip=(960-60)/1.0=900, topDip=(520-140)/1.0=380.
        var (left, top) = WindowPositionAdapter.RecoverToPrimary(monitors, WinW, WinH);

        Assert.AreEqual(900, left, 0.01);
        Assert.AreEqual(380, top, 0.01, "Should center in taskbar-reduced work area");
    }

    [TestMethod]
    public void RecoverToPrimary_150pct_UsesPrimaryDpi()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 2560, 1440, 1.5)
        };

        // Work area center: (1280, 720).
        // Window phys: 120*1.5=180, 140*1.5=210.
        // minFootX=90, maxFootX=2470, minFootY=210, maxFootY=1440.
        // 1280 in [90,2470], 720 in [210,1440] → no clamp.
        // Reverse DPI 1.5: leftDip=(1280-90)/1.5=793.33, topDip=(720-210)/1.5=340.
        var (left, top) = WindowPositionAdapter.RecoverToPrimary(monitors, WinW, WinH);

        Assert.AreEqual(793.33, left, 0.1, "Should use 150% DPI for reverse conversion");
        Assert.AreEqual(340, top, 0.1, "Should use 150% DPI for reverse conversion");
    }

    // ── Edge cases ───────────────────────────────────────────

    [TestMethod]
    public void Clamp_EmptyMonitorList_ReturnsOriginalPosition()
    {
        var monitors = new List<MonitorInfo>();

        var (left, top) = WindowPositionAdapter.ClampToWorkArea(
            monitors, 100, 200, WinW, WinH, 1.0, 1.0);

        Assert.AreEqual(100, left, 0.01);
        Assert.AreEqual(200, top, 0.01);
    }

    [TestMethod]
    public void Recover_EmptyMonitorList_ReturnsOriginalPosition()
    {
        var monitors = new List<MonitorInfo>();

        var (left, top) = WindowPositionAdapter.RecoverPosition(
            monitors, 100, 200, WinW, WinH, 1.0, 1.0, null);

        Assert.AreEqual(100, left, 0.01);
        Assert.AreEqual(200, top, 0.01);
    }

    [TestMethod]
    public void RecoverToPrimary_EmptyMonitorList_ReturnsZero()
    {
        var monitors = new List<MonitorInfo>();

        var (left, top) = WindowPositionAdapter.RecoverToPrimary(monitors, WinW, WinH);

        Assert.AreEqual(0, left, 0.01);
        Assert.AreEqual(0, top, 0.01);
    }

    [TestMethod]
    public void Clamp_WindowLargerThanWorkArea_ClampsToMinimum()
    {
        // Tiny monitor 200×200, window 120×140 DIP at 100%.
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("MON1", 0, 0, 200, 200, 1.0)
        };

        // Window phys = 120×140. Work area 0..200 × 0..200.
        // minFootX = 60, maxFootX = 200-60 = 140.
        // minFootY = 140, maxFootY = 200.
        // Place at DIP (500, 500) → foot = (560, 640).
        // Clamped to (140, 200). Reverse DPI 1.0: (80, 60).
        var (left, top) = WindowPositionAdapter.ClampToWorkArea(
            monitors, 500, 500, WinW, WinH, 1.0, 1.0);

        Assert.AreEqual(80, left, 0.01, "Should clamp to maxFootX when window is large");
        Assert.AreEqual(60, top, 0.01, "Should clamp to maxFootY when window is large");
    }
}
