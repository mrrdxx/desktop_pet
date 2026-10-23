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

    // ── G-02: Stay must correspond to last drag release ────────────────

    [TestMethod]
    public void NewDragCancelsPreviousStay()
    {
        // G-02: A new drag end must cancel any previous stay.
        // The 30-second stay must be continuous after the FINAL release.
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordStayStart(0); // Start staying after first drag.

        // Second drag at 60s — should cancel the first stay.
        tracker.RecordCornerDrag(60000);
        Assert.IsFalse(tracker.IsStayActive, "Stay should be cancelled by new drag.");

        // Third drag at 120s.
        tracker.RecordCornerDrag(120000);
        tracker.RecordStayStart(120000);

        // 30 seconds after the THIRD drag's stay start.
        Assert.IsTrue(tracker.IsConditionMet(150000));
    }

    [TestMethod]
    public void OldStayBeforeLastDrag_DoesNotCount()
    {
        // G-02: Stay started before the last drag end must not count.
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        // Start stay after second drag.
        tracker.RecordStayStart(60000);

        // Third drag at 120s — cancels the stay.
        tracker.RecordCornerDrag(120000);

        // Now try to use the old stay time (before third drag).
        // RecordStayStart with a time before _lastDragEndTime should still
        // set _lastDragStayStart, but IsConditionMet checks ordering.
        // Actually, RecordStayStart just sets the time; IsConditionMet
        // checks _lastDragStayStart >= _lastDragEndTime.
        // Since we didn't call RecordStayStart after the third drag,
        // _lastDragStayStart is null (cleared by RecordCornerDrag).
        Assert.IsFalse(tracker.IsConditionMet(150000),
            "No stay after third drag — condition should not be met.");
    }

    [TestMethod]
    public void ThirdDragStayUnder30Seconds_NotMet()
    {
        // G-02: Third drag release, stay starts, but only 20 seconds — not met.
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordCornerDrag(120000);
        tracker.RecordStayStart(120000);

        // Only 20 seconds of stay.
        Assert.IsFalse(tracker.IsConditionMet(140000));
    }

    [TestMethod]
    public void CancelStay_ThenNoCondition()
    {
        // G-02: Leaving the corner cancels stay; condition cannot be met
        // until a new stay starts after the last drag.
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordCornerDrag(120000);
        tracker.RecordStayStart(120000);

        // Pet leaves corner at 130s.
        tracker.CancelStay();

        Assert.IsFalse(tracker.IsConditionMet(150000));
        Assert.IsFalse(tracker.IsStayActive);
    }

    [TestMethod]
    public void CancelStay_ThenRestartStay_CanMeetCondition()
    {
        // After cancel, a new stay can start and eventually meet condition.
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordCornerDrag(120000);
        tracker.RecordStayStart(120000);
        tracker.CancelStay();

        // Pet returns to corner at 130s, stay restarts.
        tracker.RecordStayStart(130000);

        // 30 seconds from new stay start = 160000.
        Assert.IsFalse(tracker.IsConditionMet(159000));
        Assert.IsTrue(tracker.IsConditionMet(160000));
    }

    [TestMethod]
    public void FullSequence_ThreeDragsWithInterruption_OnlyLastStayCounts()
    {
        // G-02 integration: simulate realistic drag-stay-drag-stay-drag-stay sequence.
        var tracker = new CornerTracker();

        // Drag 1 at 0s, stay starts.
        tracker.RecordCornerDrag(0);
        tracker.RecordStayStart(0);

        // 20 seconds pass, then drag 2 at 20s — cancels first stay.
        tracker.RecordCornerDrag(20000);
        // Stay starts again after drag 2.
        tracker.RecordStayStart(20000);

        // 15 seconds pass, then drag 3 at 35s — cancels second stay.
        tracker.RecordCornerDrag(35000);
        // Stay starts after drag 3.
        tracker.RecordStayStart(35000);

        // 29 seconds after drag 3 stay — not enough.
        Assert.IsFalse(tracker.IsConditionMet(64000));

        // 30 seconds after drag 3 stay — condition met.
        Assert.IsTrue(tracker.IsConditionMet(65000));
    }

    [TestMethod]
    public void NoDoubleAward_AfterConditionMetAndClear()
    {
        // G-02: After condition is met and Clear is called,
        // subsequent ticks without new drags should not re-award.
        var tracker = new CornerTracker();
        tracker.RecordCornerDrag(0);
        tracker.RecordCornerDrag(60000);
        tracker.RecordCornerDrag(120000);
        tracker.RecordStayStart(120000);

        Assert.IsTrue(tracker.IsConditionMet(150000));
        tracker.Clear();

        // No new drags — condition should not be met again.
        Assert.IsFalse(tracker.IsConditionMet(160000));
        Assert.IsFalse(tracker.IsConditionMet(200000));
    }

    [TestMethod]
    public void RecordStayStart_BeforeAnyDrag_Ignored()
    {
        // Edge case: stay start before any drag should be ignored.
        var tracker = new CornerTracker();
        tracker.RecordStayStart(0);

        Assert.IsFalse(tracker.IsStayActive);
        Assert.IsFalse(tracker.IsConditionMet(30000));
    }
}
