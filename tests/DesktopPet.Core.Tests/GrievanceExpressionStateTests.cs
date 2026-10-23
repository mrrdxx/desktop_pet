using DesktopPet.Core.Emotion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// GrievanceExpressionState pending, trigger, interrupt, and restore tests.
/// Rules:
///   - Mark pending when grievance reaches threshold, record local date.
///   - Fire on next local day (different date from marked date).
///   - Clear after expression completes or is interrupted by drag.
///   - Offline decay does not cancel pending state.
///   - Restore from archive on restart.
/// </summary>
[TestClass]
public class GrievanceExpressionStateTests
{
    // ── Marking pending ────────────────────────────────────────────────

    [TestMethod]
    public void TryMarkPending_ThresholdReached_MarksPending()
    {
        var state = new GrievanceExpressionState();
        var result = state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        Assert.IsTrue(result);
        Assert.IsTrue(state.IsPending);
        Assert.AreEqual("2026-10-23", state.MarkedDate);
    }

    [TestMethod]
    public void TryMarkPending_ThresholdNotReached_DoesNotMark()
    {
        var state = new GrievanceExpressionState();
        var result = state.TryMarkPending("2026-10-23", grievanceReachedThreshold: false);

        Assert.IsFalse(result);
        Assert.IsFalse(state.IsPending);
        Assert.IsNull(state.MarkedDate);
    }

    [TestMethod]
    public void TryMarkPending_AlreadyPending_DoesNotRemark()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        // Second call should not re-mark or change date.
        var result = state.TryMarkPending("2026-10-24", grievanceReachedThreshold: true);

        Assert.IsFalse(result);
        Assert.AreEqual("2026-10-23", state.MarkedDate);
    }

    // ── Trigger on next day ────────────────────────────────────────────

    [TestMethod]
    public void ShouldExpress_DifferentDay_ReturnsTrue()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        Assert.IsTrue(state.ShouldExpress("2026-10-24"));
    }

    [TestMethod]
    public void ShouldExpress_SameDay_ReturnsFalse()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        Assert.IsFalse(state.ShouldExpress("2026-10-23"));
    }

    [TestMethod]
    public void ShouldExpress_NotPending_ReturnsFalse()
    {
        var state = new GrievanceExpressionState();

        Assert.IsFalse(state.ShouldExpress("2026-10-24"));
    }

    // ── Clear after expression ─────────────────────────────────────────

    [TestMethod]
    public void Clear_ResetsPendingState()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        state.Clear();

        Assert.IsFalse(state.IsPending);
        Assert.IsNull(state.MarkedDate);
    }

    [TestMethod]
    public void Clear_AfterExpression_CanRemarkLater()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);
        state.Clear();

        // New grievance threshold reached on a later day.
        var result = state.TryMarkPending("2026-10-25", grievanceReachedThreshold: true);

        Assert.IsTrue(result);
        Assert.AreEqual("2026-10-25", state.MarkedDate);
    }

    // ── Drag interrupt ─────────────────────────────────────────────────

    [TestMethod]
    public void DragInterrupt_ClearsPending_NoSameDayReexpress()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        // Expression starts on next day, then drag interrupts.
        Assert.IsTrue(state.ShouldExpress("2026-10-24"));
        state.Clear();

        // Should not re-express on the same day.
        Assert.IsFalse(state.ShouldExpress("2026-10-24"));
    }

    // ── Offline decay does not cancel pending ──────────────────────────

    [TestMethod]
    public void OfflineDecay_DoesNotCancelPending()
    {
        // Even if grievance decays below threshold during offline period,
        // the pending state should remain.
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        // Simulate offline period — pending state is not touched by decay.
        // On restart, ShouldExpress should still fire on a different day.
        Assert.IsTrue(state.ShouldExpress("2026-10-25"));
    }

    // ── Restore from archive ───────────────────────────────────────────

    [TestMethod]
    public void Restore_PendingWithDate_RestoresCorrectly()
    {
        var state = new GrievanceExpressionState();
        state.Restore(pending: true, markedDate: "2026-10-22");

        Assert.IsTrue(state.IsPending);
        Assert.AreEqual("2026-10-22", state.MarkedDate);
        // Next day should trigger.
        Assert.IsTrue(state.ShouldExpress("2026-10-23"));
    }

    [TestMethod]
    public void Restore_NotPending_RestoresCorrectly()
    {
        var state = new GrievanceExpressionState();
        state.Restore(pending: false, markedDate: null);

        Assert.IsFalse(state.IsPending);
        Assert.IsNull(state.MarkedDate);
        Assert.IsFalse(state.ShouldExpress("2026-10-23"));
    }

    [TestMethod]
    public void Restore_PendingSameDay_DoesNotExpress()
    {
        var state = new GrievanceExpressionState();
        state.Restore(pending: true, markedDate: "2026-10-23");

        // Same day as marked — should not express.
        Assert.IsFalse(state.ShouldExpress("2026-10-23"));
        // Next day — should express.
        Assert.IsTrue(state.ShouldExpress("2026-10-24"));
    }

    // ── Restart does not re-award ──────────────────────────────────────

    [TestMethod]
    public void Restore_Pending_TryMarkDoesNotRemark()
    {
        var state = new GrievanceExpressionState();
        state.Restore(pending: true, markedDate: "2026-10-22");

        // After restore, even if threshold is still reached, should not re-mark.
        var result = state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        Assert.IsFalse(result);
        Assert.AreEqual("2026-10-22", state.MarkedDate);
    }

    // ── F-01: Clock rollback does not trigger expression ───────────────

    [TestMethod]
    public void ShouldExpress_ClockRollbackEarlierDate_DoesNotTrigger()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        // Clock rolled back to previous day — must NOT trigger.
        Assert.IsFalse(state.ShouldExpress("2026-10-22"));
    }

    [TestMethod]
    public void ShouldExpress_ClockRollbackSameDate_DoesNotTrigger()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        // Same date — must NOT trigger (not "next day").
        Assert.IsFalse(state.ShouldExpress("2026-10-23"));
    }

    [TestMethod]
    public void ShouldExpress_LaterDate_Triggers()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        // Two days later — should trigger.
        Assert.IsTrue(state.ShouldExpress("2026-10-25"));
    }

    [TestMethod]
    public void ShouldExpress_MonthBoundary_Triggers()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-31", grievanceReachedThreshold: true);

        // Next day is November 1st — should trigger.
        Assert.IsTrue(state.ShouldExpress("2026-11-01"));
    }

    [TestMethod]
    public void ShouldExpress_YearBoundary_Triggers()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-12-31", grievanceReachedThreshold: true);

        // Next day is January 1st of next year — should trigger.
        Assert.IsTrue(state.ShouldExpress("2027-01-01"));
    }

    // ── F-01: Invalid / missing date recovery ──────────────────────────

    [TestMethod]
    public void ShouldExpress_InvalidCurrentDate_DoesNotTrigger()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        // Garbage current date — must not trigger.
        Assert.IsFalse(state.ShouldExpress("not-a-date"));
    }

    [TestMethod]
    public void ShouldExpress_EmptyCurrentDate_DoesNotTrigger()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        Assert.IsFalse(state.ShouldExpress(""));
    }

    [TestMethod]
    public void ShouldExpress_NullCurrentDate_DoesNotTrigger()
    {
        var state = new GrievanceExpressionState();
        state.TryMarkPending("2026-10-23", grievanceReachedThreshold: true);

        Assert.IsFalse(state.ShouldExpress(null!));
    }

    [TestMethod]
    public void ShouldExpress_InvalidMarkedDate_DoesNotTrigger()
    {
        var state = new GrievanceExpressionState();
        // Simulate corrupted archive with invalid marked date.
        state.Restore(pending: true, markedDate: "corrupted");

        // Even with a valid current date, invalid marked date must not trigger.
        Assert.IsFalse(state.ShouldExpress("2026-10-24"));
    }

    [TestMethod]
    public void ShouldExpress_NullMarkedDate_DoesNotTrigger()
    {
        var state = new GrievanceExpressionState();
        // Pending but marked date is null (shouldn't normally happen, but test recovery).
        state.Restore(pending: true, markedDate: null);

        Assert.IsFalse(state.ShouldExpress("2026-10-24"));
    }

    [TestMethod]
    public void Restore_InvalidMarkedDate_CanBeRemarkWithValidDate()
    {
        var state = new GrievanceExpressionState();
        state.Restore(pending: true, markedDate: "corrupted");

        // ShouldExpress won't fire with corrupted date.
        Assert.IsFalse(state.ShouldExpress("2026-10-24"));

        // Clear and re-mark with valid date should work.
        state.Clear();
        var result = state.TryMarkPending("2026-10-24", grievanceReachedThreshold: true);
        Assert.IsTrue(result);
        Assert.IsTrue(state.ShouldExpress("2026-10-25"));
    }

    // ── F-01: Rollback after restore ───────────────────────────────────

    [TestMethod]
    public void Restore_PendingThenRollback_DoesNotTrigger()
    {
        var state = new GrievanceExpressionState();
        state.Restore(pending: true, markedDate: "2026-10-23");

        // Clock rollback to before marked date.
        Assert.IsFalse(state.ShouldExpress("2026-10-22"));
        // Same date.
        Assert.IsFalse(state.ShouldExpress("2026-10-23"));
        // Next day still works.
        Assert.IsTrue(state.ShouldExpress("2026-10-24"));
    }
}
