using DesktopPet.Core.Emotion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// CornerTracker window, condition, and anti-false-positive tests.
/// Rules:
///   - 5-minute window, >= 3 corner drags, last stay >= 30 seconds.
///   - One-time +20 after condition met, then caller clears.
///   - Not persisted across restarts.
/// </summary>
[TestClass]
public class CornerTrackerTests
{
    // All times in milliseconds. 1 min = 60000 ms, 30 sec = 30000 ms.

    // ── Basic condition ────────────────────────────────────────────────

    [TestMethod]
    public void ThreeDrags_WithStay_ConditionMet()
    {
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordCornerDrag(120000);
        tracker.RecordStayStart(120000);

        // 30 seconds after stay start.
        Assert.IsTrue(tracker.IsConditionMet(150000));
    }

    [TestMethod]
    public void TwoDrags_ConditionNotMet()
    {
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordStayStart(60000);

        Assert.IsFalse(tracker.IsConditionMet(90000));
    }

    [TestMethod]
    public void ThreeDrags_NoStay_ConditionNotMet()
    {
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordCornerDrag(120000);
        // No RecordStayStart called.

        Assert.IsFalse(tracker.IsConditionMet(150000));
    }

    [TestMethod]
    public void ThreeDrags_StayUnder30Seconds_ConditionNotMet()
    {
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordCornerDrag(120000);
        tracker.RecordStayStart(120000);

        // Only 29 seconds.
        Assert.IsFalse(tracker.IsConditionMet(149000));
    }

    // ── Window expiry ──────────────────────────────────────────────────

    [TestMethod]
    public void DragsOutsideWindow_Pruned()
    {
        var tracker = new CornerTracker();
        // First drag at 0, second at 30s — both will be outside the
        // 5-min window when the third drag happens at 6 minutes.
        // Window at t=360000: [60000, 360000].
        // 0 < 60000 → pruned. 30000 < 60000 → pruned. 360000 remains.
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(30000);
        tracker.RecordCornerDrag(360000);
        tracker.RecordStayStart(360000);

        // Only 1 drag remains in window.
        Assert.IsFalse(tracker.IsConditionMet(390000));
        Assert.AreEqual(1, tracker.CornerDragCount);
    }

    [TestMethod]
    public void AllDragsExpired_StateReset()
    {
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordStayStart(0);

        // 10 minutes later — all expired.
        tracker.RecordCornerDrag(600000);
        Assert.AreEqual(1, tracker.CornerDragCount);
    }

    // ── No false positive from normal drag ─────────────────────────────

    [TestMethod]
    public void SingleCornerDrag_NoGrievance()
    {
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordStayStart(0);

        Assert.IsFalse(tracker.IsConditionMet(60000));
    }

    [TestMethod]
    public void EmptyTracker_NeverConditionMet()
    {
        var tracker = new CornerTracker();
        Assert.IsFalse(tracker.IsConditionMet(100000));
        Assert.AreEqual(0, tracker.CornerDragCount);
    }

    // ── Clear after award ──────────────────────────────────────────────

    [TestMethod]
    public void Clear_ResetsAllState()
    {
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordCornerDrag(120000);
        tracker.RecordStayStart(120000);
        Assert.IsTrue(tracker.IsConditionMet(150000));

        tracker.Clear();

        Assert.AreEqual(0, tracker.CornerDragCount);
        Assert.IsFalse(tracker.IsConditionMet(150000));
    }

    [TestMethod]
    public void AfterClear_NewSequence_CanMeetConditionAgain()
    {
        var tracker = new CornerTracker();
        // First sequence.
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordCornerDrag(120000);
        tracker.RecordStayStart(120000);
        Assert.IsTrue(tracker.IsConditionMet(150000));
        tracker.Clear();

        // Second sequence after clear.
        tracker.RecordCornerDrag(200000);
        tracker.RecordCornerDrag(260000);
        tracker.RecordCornerDrag(320000);
        tracker.RecordStayStart(320000);
        Assert.IsTrue(tracker.IsConditionMet(350000));
    }

    // ── No double-award within same window ─────────────────────────────

    [TestMethod]
    public void ConditionMet_DoesNotAutoClear_CallerMustClear()
    {
        // IsConditionMet should remain true until Clear is called,
        // so the caller is responsible for ensuring single award.
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordCornerDrag(120000);
        tracker.RecordStayStart(120000);

        Assert.IsTrue(tracker.IsConditionMet(150000));
        // Still true without clear.
        Assert.IsTrue(tracker.IsConditionMet(160000));
    }

    // ── Stay from different drag ───────────────────────────────────────

    [TestMethod]
    public void StayStart_OverwritesPrevious()
    {
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordCornerDrag(120000);
        tracker.RecordStayStart(120000);

        // 20 seconds — not enough.
        Assert.IsFalse(tracker.IsConditionMet(140000));

        // New stay start at 140000.
        tracker.RecordStayStart(140000);
        // 30 seconds from new stay start.
        Assert.IsTrue(tracker.IsConditionMet(170000));
    }
}
