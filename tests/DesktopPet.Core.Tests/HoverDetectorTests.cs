using DesktopPet.Core.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// Tests for HoverDetector: dwell trigger, single-fire per session,
/// leave-and-return reset, and interrupt reset.
///
/// Uses a controlled clock (explicit timestamps) and fixed input
/// sequences — no real mouse or timer dependencies.
/// </summary>
[TestClass]
public class HoverDetectorTests
{
    private static PetRectPx Rect(double left, double top, double w, double h) =>
        new(left, top, w, h);

    // Pet at (100,100) size 120x120 → covers [100,220] x [100,220]
    private static readonly PetRectPx _petRect = Rect(100, 100, 120, 120);

    [TestMethod]
    public void Leave_AtHalfSecond_EmitsOnceAndEndsSession()
    {
        var det = new HoverDetector();
        det.Update(150, 150, _petRect, 0);
        Assert.AreEqual(HoverTrigger.Triggered, det.Update(150, 150, _petRect, 1200));
        Assert.AreEqual(HoverTrigger.None, det.Update(500, 500, _petRect, 1300));
        Assert.AreEqual(HoverTrigger.None, det.Update(500, 500, _petRect, 1799));
        Assert.AreEqual(HoverTrigger.Left, det.Update(500, 500, _petRect, 1800));
        Assert.IsFalse(det.IsHovering);
        Assert.IsFalse(det.HasTriggered);
        Assert.AreEqual(HoverTrigger.None, det.Update(500, 500, _petRect, 5000));
    }

    [TestMethod]
    public void BriefLeaveThenReenter_CancelsOldLeaveDeadline()
    {
        var det = new HoverDetector();
        det.Update(150, 150, _petRect, 0);
        det.Update(150, 150, _petRect, 1200);
        det.Update(500, 500, _petRect, 1300);
        Assert.AreEqual(HoverTrigger.None, det.Update(150, 150, _petRect, 1700));
        Assert.IsTrue(det.HasTriggered);
        det.Update(500, 500, _petRect, 2000);
        Assert.AreEqual(HoverTrigger.None, det.Update(500, 500, _petRect, 2499));
        Assert.AreEqual(HoverTrigger.Left, det.Update(500, 500, _petRect, 2500));
    }

    [TestMethod]
    public void ResetDuringLeave_RequiresFreshOutsideInterval_AndStillReportsLeft()
    {
        var det = new HoverDetector();
        det.Update(150, 150, _petRect, 0);
        det.Update(150, 150, _petRect, 1200);
        det.Update(500, 500, _petRect, 1300);
        det.Reset();
        det.Update(500, 500, _petRect, 2000);
        Assert.AreEqual(HoverTrigger.None, det.Update(500, 500, _petRect, 2499));
        Assert.AreEqual(HoverTrigger.Left, det.Update(500, 500, _petRect, 2500),
            "Leave must be reported even after an interruption cleared the old triggered flag.");
    }

    [TestMethod]
    public void TransparentPointInsideBounds_CountsAsLeave()
    {
        var det = new HoverDetector();
        det.Update(150, 150, _petRect, 0, true);
        det.Update(150, 150, _petRect, 1200, true);
        det.Update(150, 150, _petRect, 1300, false);
        Assert.AreEqual(HoverTrigger.Left, det.Update(150, 150, _petRect, 1800, false));
        Assert.AreEqual(HoverTrigger.None, det.Update(150, 150, _petRect, 1900, true));
        Assert.AreEqual(HoverTrigger.Triggered, det.Update(150, 150, _petRect, 3100, true));
    }

    // ── Basic dwell trigger ──

    [TestMethod]
    public void Hover_DwellReached_TriggersOnce()
    {
        var det = new HoverDetector(new HoverDetectorConfig
        {
            DwellMs = 1200,
            LeaveResetMs = 500,
        });

        // Cursor enters at t=0, stays inside.
        var r0 = det.Update(150, 150, _petRect, 0);
        Assert.AreEqual(HoverTrigger.None, r0);

        // Just before threshold — no trigger.
        var r1 = det.Update(150, 150, _petRect, 1199);
        Assert.AreEqual(HoverTrigger.None, r1);

        // At threshold — trigger.
        var r2 = det.Update(150, 150, _petRect, 1200);
        Assert.AreEqual(HoverTrigger.Triggered, r2);

        // Continue dwelling — no re-trigger (single fire).
        var r3 = det.Update(150, 150, _petRect, 2000);
        Assert.AreEqual(HoverTrigger.None, r3);
    }

    // ── Leave and return reset ──

    [TestMethod]
    public void Hover_LeaveThenReturn_AfterReset_CanTriggerAgain()
    {
        var det = new HoverDetector(new HoverDetectorConfig
        {
            DwellMs = 1200,
            LeaveResetMs = 500,
        });

        // First hover: trigger at t=1200.
        det.Update(150, 150, _petRect, 0);
        var r1 = det.Update(150, 150, _petRect, 1200);
        Assert.AreEqual(HoverTrigger.Triggered, r1);

        // Leave at t=1300.
        det.Update(500, 500, _petRect, 1300);

        // Not enough time yet (only 200ms since leave at t=1300, need 500ms).
        det.Update(500, 500, _petRect, 1400);

        // Return at t=1900 — 600ms since last inside (1300), exceeds 500ms reset.
        // Should start a fresh dwell.
        var r2 = det.Update(150, 150, _petRect, 1900);
        Assert.AreEqual(HoverTrigger.Left, r2, "Report the completed leave if no tick observed its timeout outside.");

        // Dwell again for 1200ms → trigger.
        var r3 = det.Update(150, 150, _petRect, 3100);
        Assert.AreEqual(HoverTrigger.Triggered, r3);
    }

    // ── Quick leave and return does NOT reset ──

    [TestMethod]
    public void Hover_QuickLeaveAndReturn_NoReset()
    {
        var det = new HoverDetector(new HoverDetectorConfig
        {
            DwellMs = 1200,
            LeaveResetMs = 500,
        });

        // Start dwelling at t=0.
        det.Update(150, 150, _petRect, 0);

        // Briefly leave at t=100, return at t=200 (only 100ms outside, < 500ms).
        det.Update(500, 500, _petRect, 100);
        det.Update(150, 150, _petRect, 200);

        // The dwell should NOT have been reset because the leave was too brief.
        // However, _lastInsideMs was updated to 200, and _dwellStartMs is still 0.
        // At t=1200, dwell from t=0 is 1200ms → should trigger.
        var r = det.Update(150, 150, _petRect, 1200);
        Assert.AreEqual(HoverTrigger.Triggered, r);
    }

    // ── Continuous hover only triggers once ──

    [TestMethod]
    public void Hover_ContinuousDwell_TriggersOnlyOnce()
    {
        var det = new HoverDetector();
        HoverTrigger result = HoverTrigger.None;

        for (int t = 0; t <= 5000; t += 33)
        {
            var r = det.Update(150, 150, _petRect, t);
            if (r == HoverTrigger.Triggered)
            {
                // Should only trigger once.
                Assert.AreNotEqual(HoverTrigger.Triggered, result,
                    "Hover triggered more than once in continuous dwell");
                result = r;
            }
        }

        Assert.AreEqual(HoverTrigger.Triggered, result,
            "Hover should have triggered at least once");
    }

    // ── Reset clears state ──

    [TestMethod]
    public void Hover_Reset_ClearsState()
    {
        var det = new HoverDetector(new HoverDetectorConfig
        {
            DwellMs = 1000,
            LeaveResetMs = 500,
        });

        // Start dwelling, trigger.
        det.Update(150, 150, _petRect, 0);
        var r1 = det.Update(150, 150, _petRect, 1000);
        Assert.AreEqual(HoverTrigger.Triggered, r1);

        // Reset (simulating drag/pause).
        det.Reset();
        Assert.IsFalse(det.IsHovering);
        Assert.IsFalse(det.HasTriggered);

        // After reset, dwelling again should trigger.
        det.Update(150, 150, _petRect, 2000);
        var r2 = det.Update(150, 150, _petRect, 3000);
        Assert.AreEqual(HoverTrigger.Triggered, r2);
    }

    // ── Cursor outside rectangle does not trigger ──

    [TestMethod]
    public void Hover_CursorOutside_NoTrigger()
    {
        var det = new HoverDetector(new HoverDetectorConfig
        {
            DwellMs = 1000,
        });

        // Cursor far away for a long time.
        var r = det.Update(1000, 1000, _petRect, 0);
        det.Update(1000, 1000, _petRect, 5000);
        Assert.AreEqual(HoverTrigger.None, r);
        Assert.IsFalse(det.IsHovering);
    }

    // ── Hover margin ──

    [TestMethod]
    public void Hover_WithMargin_TriggersWhenNearButOutside()
    {
        var det = new HoverDetector(new HoverDetectorConfig
        {
            DwellMs = 1000,
            HoverMarginPx = 20,
        });

        // Cursor 10px outside the right edge (220 + 10 = 230), within margin 20.
        det.Update(230, 150, _petRect, 0);
        var r = det.Update(230, 150, _petRect, 1000);
        Assert.AreEqual(HoverTrigger.Triggered, r);
    }

    // ── F-02: Continuous outside sampling then return can re-trigger ──

    [TestMethod]
    public void Hover_ContinuousOutsideSampling_ThenReturn_CanRetrigger()
    {
        // Reproduces the监理 F-02 反例:
        // t=0 enter, t=1200 first Triggered;
        // t=1233..2223 continuous outside every 33ms (30 samples);
        // t=2256 return inside;
        // t=3456 dwell 1200ms → should Triggered again.
        //
        // The bug: _lastOutsideMs was overwritten every outside tick,
        // so (returnTime - _lastOutsideMs) was only ~33ms, never reaching
        // LeaveResetMs=500. Fix: record _leaveStartMs only on the
        // inside→outside transition, not on every outside sample.

        var det = new HoverDetector(new HoverDetectorConfig
        {
            DwellMs = 1200,
            LeaveResetMs = 500,
        });

        // Enter and trigger first hover.
        det.Update(150, 150, _petRect, 0);
        var r1 = det.Update(150, 150, _petRect, 1200);
        Assert.AreEqual(HoverTrigger.Triggered, r1);

        // Continuous outside sampling every 33ms from t=1233 to t=2223.
        for (int t = 1233; t <= 2223; t += 33)
        {
            det.Update(500, 500, _petRect, t);
        }

        // Return inside at t=2256. Leave started at t=1233, so
        // 2256 - 1233 = 1023ms >= 500ms → session should reset.
        var rReturn = det.Update(150, 150, _petRect, 2256);
        Assert.AreEqual(HoverTrigger.None, rReturn);

        // Dwell 1200ms → should trigger again.
        var r2 = det.Update(150, 150, _petRect, 3456);
        Assert.AreEqual(HoverTrigger.Triggered, r2,
            "Should re-trigger after continuous outside sampling + return + dwell");
    }

    // ── F-02 variant: Short continuous outside (< LeaveResetMs) does NOT reset ──

    [TestMethod]
    public void Hover_ContinuousOutsideShort_DoesNotReset()
    {
        // Complement to the above: if continuous outside is shorter than
        // LeaveResetMs, the session should NOT reset and the original
        // dwell (already triggered) stays triggered (no re-trigger).

        var det = new HoverDetector(new HoverDetectorConfig
        {
            DwellMs = 1200,
            LeaveResetMs = 500,
        });

        // Enter and trigger.
        det.Update(150, 150, _petRect, 0);
        var r1 = det.Update(150, 150, _petRect, 1200);
        Assert.AreEqual(HoverTrigger.Triggered, r1);

        // Continuous outside for only 300ms (< 500ms LeaveResetMs).
        for (int t = 1233; t <= 1533; t += 33)
        {
            det.Update(500, 500, _petRect, t);
        }

        // Return at t=1566. Leave started at t=1233, 1566-1233=333ms < 500ms.
        // Session NOT reset, _hasTriggered still true.
        det.Update(150, 150, _petRect, 1566);

        // Continue dwelling — should NOT re-trigger because session wasn't reset.
        var r2 = det.Update(150, 150, _petRect, 3000);
        Assert.AreEqual(HoverTrigger.None, r2,
            "Should NOT re-trigger if leave was shorter than LeaveResetMs");
    }
}
