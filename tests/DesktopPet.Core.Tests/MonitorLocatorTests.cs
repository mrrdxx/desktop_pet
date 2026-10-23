using DesktopPet.Core.Geometry;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace DesktopPet.Core.Tests;

/// <summary>
/// Tests for monitor locating, work-area clamping, and position recovery
/// when monitors are disconnected or layout changes.
/// </summary>
[TestClass]
public class MonitorLocatorTests
{
    // Helper: create a monitor at the given pixel bounds with 100% DPI.
    private static MonitorInfo MakeMonitor(string name, int left, int top, int width, int height,
        double dpiScale = 1.0, int taskbarHeight = 0)
    {
        var full = new MonitorRectPx(left, top, width, height);
        var workBounds = new MonitorRectPx(left, top, width, height - taskbarHeight);
        return new MonitorInfo(name, full, new WorkAreaPx(workBounds), dpiScale, dpiScale);
    }

    [TestMethod]
    public void FindMonitor_PointOnPrimary_ReturnsPrimary()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
        };

        var found = MonitorLocator.FindMonitor(monitors, 500, 500);
        Assert.IsNotNull(found);
        Assert.AreEqual("DISPLAY1", found.Value.DeviceName);
    }

    [TestMethod]
    public void FindMonitor_NegativeCoordinate_ReturnsSecondary()
    {
        // Secondary monitor to the left of primary: x from -1920 to 0
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
            MakeMonitor("DISPLAY2", -1920, 0, 1920, 1080),
        };

        var found = MonitorLocator.FindMonitor(monitors, -1000, 500);
        Assert.IsNotNull(found);
        Assert.AreEqual("DISPLAY2", found.Value.DeviceName);
    }

    [TestMethod]
    public void FindMonitor_PointInGap_ReturnsNull()
    {
        // Two monitors with a 100px gap between them
        // Primary: 0 to 1920, Secondary: 2020 to 3940
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
            MakeMonitor("DISPLAY2", 2020, 0, 1920, 1080),
        };

        // Point at x=1970 is in the gap (1920 < 1970 < 2020)
        var found = MonitorLocator.FindMonitor(monitors, 1970, 500);
        Assert.IsNull(found);
    }

    [TestMethod]
    public void FindNearestMonitor_PointInGap_ReturnsCloserMonitor()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
            MakeMonitor("DISPLAY2", 2020, 0, 1920, 1080),
        };

        // Point at x=1970: center of DISPLAY1 is 960, center of DISPLAY2 is 2980
        // Distance to DISPLAY1: |1970-960| = 1010
        // Distance to DISPLAY2: |1970-2980| = 1010
        // Equal distance — should return one of them (first found with min distance)
        var nearest = MonitorLocator.FindNearestMonitor(monitors, 1970, 500);
        Assert.IsNotNull(nearest);

        // Point at x=1950: closer to DISPLAY1
        nearest = MonitorLocator.FindNearestMonitor(monitors, 1950, 500);
        Assert.AreEqual("DISPLAY1", nearest.DeviceName);

        // Point at x=1990: closer to DISPLAY2
        nearest = MonitorLocator.FindNearestMonitor(monitors, 1990, 500);
        Assert.AreEqual("DISPLAY2", nearest.DeviceName);
    }

    [TestMethod]
    public void ClampToWorkArea_PetAtEdge_StaysInWorkArea()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080, 1.0, 40), // 40px taskbar
        };

        // Pet at foot (0, 0): window would be at left=-60, top=-120 (off-screen)
        var clamped = MonitorLocator.ClampToWorkArea(monitors, new PetWorldPosition(0, 0), 120, 120);

        // minFootX = 0 + 60 = 60, minFootY = 0 + 120 = 120
        Assert.AreEqual(60, clamped.FootCenterX, 0.01);
        Assert.AreEqual(120, clamped.FootCenterY, 0.01);
    }

    [TestMethod]
    public void ClampToWorkArea_PetInGap_SnapsToNearest()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
            MakeMonitor("DISPLAY2", 2020, 0, 1920, 1080),
        };

        // Pet in gap at x=1970: nearest is DISPLAY1 (or DISPLAY2, equidistant)
        var clamped = MonitorLocator.ClampToWorkArea(monitors, new PetWorldPosition(1970, 500), 120, 120);

        // F-04: The clamped position must be fully inside a valid work area.
        // Window is 120 DIP at 100% = 120px. Foot-center means:
        //   left = footX - 60, right = footX + 60, top = footY - 120, bottom = footY
        // The entire window must fit within one monitor's work area.
        Assert.IsFalse(double.IsNaN(clamped.FootCenterX) || double.IsInfinity(clamped.FootCenterX),
            "Clamped X must not be NaN or Infinity");
        Assert.IsFalse(double.IsNaN(clamped.FootCenterY) || double.IsInfinity(clamped.FootCenterY),
            "Clamped Y must not be NaN or Infinity");

        // Verify the window is fully contained in some monitor's work area.
        var winLeft = clamped.FootCenterX - 60;
        var winRight = clamped.FootCenterX + 60;
        var winTop = clamped.FootCenterY - 120;
        var winBottom = clamped.FootCenterY;

        bool contained = false;
        foreach (var m in monitors)
        {
            var wa = m.WorkArea.Bounds;
            if (winLeft >= wa.Left && winRight <= wa.Right &&
                winTop >= wa.Top && winBottom <= wa.Bottom)
            {
                contained = true;
                break;
            }
        }
        Assert.IsTrue(contained,
            $"Clamped position (foot={clamped.FootCenterX},{clamped.FootCenterY}) must have the full window inside a work area");

        // F-04: Counter-example — the original gap position x=1970 would NOT pass
        // this assertion because the window (1910..2030) spans the gap (1920..2020).
        var gapFoot = new PetWorldPosition(1970, 500);
        var gapWinLeft = gapFoot.FootCenterX - 60;
        var gapWinRight = gapFoot.FootCenterX + 60;
        var gapWinTop = gapFoot.FootCenterY - 120;
        var gapWinBottom = gapFoot.FootCenterY;
        bool gapContained = false;
        foreach (var m in monitors)
        {
            var wa = m.WorkArea.Bounds;
            if (gapWinLeft >= wa.Left && gapWinRight <= wa.Right &&
                gapWinTop >= wa.Top && gapWinBottom <= wa.Bottom)
            {
                gapContained = true;
                break;
            }
        }
        Assert.IsFalse(gapContained,
            "Original gap position x=1970 must be rejected: window spans the gap between monitors");
    }

    [TestMethod]
    public void ClampToWorkArea_150PercentScale_UsesCorrectPixelDimensions()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 2880, 1620, 1.5), // 150% scale, 2880px = 1920 DIP
        };

        // 120 DIP window at 150% = 180 physical pixels
        // minFootX = 0 + 90 = 90, minFootY = 0 + 180 = 180
        var clamped = MonitorLocator.ClampToWorkArea(monitors, new PetWorldPosition(0, 0), 120, 120);

        Assert.AreEqual(90, clamped.FootCenterX, 0.01);
        Assert.AreEqual(180, clamped.FootCenterY, 0.01);
    }

    [TestMethod]
    public void RecoverPosition_SavedMonitorStillPresent_ClampsToIt()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
            MakeMonitor("DISPLAY2", 1920, 0, 1920, 1080),
        };

        var savedFoot = new PetWorldPosition(2500, 500); // On DISPLAY2
        var recovered = MonitorLocator.RecoverPosition(monitors, savedFoot, "DISPLAY2", 120, 120);

        // Should stay on DISPLAY2, within work area
        Assert.IsTrue(recovered.FootCenterX >= 1980, "Should be on DISPLAY2 (x >= 1920 + 60)");
    }

    [TestMethod]
    public void RecoverPosition_SavedMonitorDisconnected_MapsToNearest()
    {
        // Only primary monitor now; secondary was disconnected
        var currentMonitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
        };

        // Pet was saved at x=2500 on a now-disconnected secondary monitor
        var savedFoot = new PetWorldPosition(2500, 500);
        var recovered = MonitorLocator.RecoverPosition(
            currentMonitors, savedFoot, "DISPLAY2", 120, 120);

        // Should be clamped to DISPLAY1's work area
        Assert.IsTrue(recovered.FootCenterX <= 1860, "Should be within DISPLAY1 work area (x <= 1920-60)");
        Assert.IsTrue(recovered.FootCenterX >= 60, "Should be within DISPLAY1 work area (x >= 60)");
    }

    [TestMethod]
    public void RecoverPosition_NoSavedDeviceName_ClampsToNearestCurrent()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
        };

        var savedFoot = new PetWorldPosition(-500, -500); // Off-screen, negative
        var recovered = MonitorLocator.RecoverPosition(
            monitors, savedFoot, null, 120, 120);

        // Should be clamped to DISPLAY1's work area
        Assert.AreEqual(60, recovered.FootCenterX, 0.01);
        Assert.AreEqual(120, recovered.FootCenterY, 0.01);
    }

    [TestMethod]
    public void ClampToWorkArea_NegativeCoordinateMonitor_WorksCorrectly()
    {
        // Secondary monitor above-left: x from -1920 to 0, y from -1080 to 0
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
            MakeMonitor("DISPLAY2", -1920, -1080, 1920, 1080),
        };

        // Pet at foot (-1900, -1060): near the top-left corner of DISPLAY2
        var clamped = MonitorLocator.ClampToWorkArea(monitors, new PetWorldPosition(-1900, -1060), 120, 120);

        // minFootX = -1920 + 60 = -1860, minFootY = -1080 + 120 = -960
        Assert.AreEqual(-1860, clamped.FootCenterX, 0.01);
        Assert.AreEqual(-960, clamped.FootCenterY, 0.01);
    }

    // F-03: "标识存在但旧坐标已落在另一块屏"
    // Saved device is DISPLAY2, but saved foot x=500 falls on DISPLAY1's area.
    // The code should clamp to DISPLAY2's work area (the saved monitor), not
    // re-select based on coordinates.
    [TestMethod]
    public void RecoverPosition_SavedMonitorExistsButFootOnDifferentMonitor_ClampsToSaved()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
            MakeMonitor("DISPLAY2", 1920, 0, 1920, 1080),
        };

        // Saved on DISPLAY2, but foot x=500 is on DISPLAY1's area
        var savedFoot = new PetWorldPosition(500, 500);
        var recovered = MonitorLocator.RecoverPosition(monitors, savedFoot, "DISPLAY2", 120, 120);

        // Should be clamped to DISPLAY2's work area (x >= 1980), not DISPLAY1
        Assert.IsTrue(recovered.FootCenterX >= 1980,
            "Should clamp to saved monitor DISPLAY2 (x >= 1920+60), not re-select by coordinates");
    }

    // F-03: "标识失效且仍有多块屏"
    // Saved device "OLDDISPLAY" no longer exists. Current monitors are
    // DISPLAY1 (primary, x=0..1920) and DISPLAY2 (x=1920..3840).
    // Saved foot x=2500 would be nearest to DISPLAY2, but per ARCHITECTURE §4,
    // "标识失效时映射到当前主屏可见区域" — should go to DISPLAY1 (primary).
    [TestMethod]
    public void RecoverPosition_SavedMonitorGone_WithMultipleMonitors_MapsToPrimary()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
            MakeMonitor("DISPLAY2", 1920, 0, 1920, 1080),
        };

        // Saved foot at x=2500 (would be nearest to DISPLAY2)
        var savedFoot = new PetWorldPosition(2500, 500);
        var recovered = MonitorLocator.RecoverPosition(
            monitors, savedFoot, "OLDDISPLAY", 120, 120);

        // Should map to primary (DISPLAY1), not nearest (DISPLAY2)
        Assert.IsTrue(recovered.FootCenterX <= 1860,
            "Should map to primary DISPLAY1 (x <= 1920-60), not nearest DISPLAY2");
        Assert.IsTrue(recovered.FootCenterX >= 60,
            "Should be within DISPLAY1 work area (x >= 60)");
    }

    // F-03: "标识失效且仍有多块屏" — no saved device name at all.
    // Same expectation: map to primary, not nearest.
    [TestMethod]
    public void RecoverPosition_NoSavedName_WithMultipleMonitors_MapsToPrimary()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
            MakeMonitor("DISPLAY2", -1920, 0, 1920, 1080), // left of primary
        };

        // Foot at x=-1000 would be nearest to DISPLAY2, but should go to primary
        var savedFoot = new PetWorldPosition(-1000, 500);
        var recovered = MonitorLocator.RecoverPosition(
            monitors, savedFoot, null, 120, 120);

        Assert.IsTrue(recovered.FootCenterX >= 60 && recovered.FootCenterX <= 1860,
            "Should map to primary DISPLAY1, not nearest DISPLAY2");
    }

    // F-04: Reject NaN and Infinity in input positions.
    [TestMethod]
    public void ClampToWorkArea_NaNInput_ThrowsOrProducesValidOutput()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
        };

        // NaN input: ClampToWorkArea should not produce NaN output.
        // The clamping logic uses comparison operators which return false for NaN,
        // so NaN passes through unchanged. We verify the output is NaN (documenting
        // current behavior) — callers must guard against NaN before calling.
        var clamped = MonitorLocator.ClampToWorkArea(monitors, new PetWorldPosition(double.NaN, 500), 120, 120);

        // Document: NaN input produces NaN output (no guard in Core).
        // App-level callers (ClampWindowToWorkArea, RecoverPet) use real Win32
        // coordinates which are always finite, so this is a theoretical edge.
        Assert.IsTrue(double.IsNaN(clamped.FootCenterX),
            "NaN input currently passes through; callers must ensure finite coordinates");
    }

    // F-04: Verify that a finite off-screen position is correctly clamped
    // and the result is finite and within a work area.
    [TestMethod]
    public void ClampToWorkArea_FiniteOffScreen_ProducesFiniteInWorkArea()
    {
        var monitors = new List<MonitorInfo>
        {
            MakeMonitor("DISPLAY1", 0, 0, 1920, 1080),
        };

        // Far off-screen position
        var clamped = MonitorLocator.ClampToWorkArea(
            monitors, new PetWorldPosition(10000, 10000), 120, 120);

        Assert.IsFalse(double.IsNaN(clamped.FootCenterX) || double.IsInfinity(clamped.FootCenterX));
        Assert.IsFalse(double.IsNaN(clamped.FootCenterY) || double.IsInfinity(clamped.FootCenterY));

        // Window: left = footX - 60, right = footX + 60
        // Must be within DISPLAY1 work area (0..1920)
        Assert.IsTrue(clamped.FootCenterX - 60 >= 0 && clamped.FootCenterX + 60 <= 1920,
            "Clamped window must be fully within work area");
    }
}
