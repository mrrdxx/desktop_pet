using DesktopPet.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// EffectiveRuntimeTracker tests for the F-02 monotonic time contract:
///   - Accumulates time from a monotonic source (not UTC wall clock).
///   - Pause: paused intervals are not counted.
///   - Clock adjustment: UTC wall clock forward/backward does not affect
///     accumulated runtime because the input is monotonic.
///   - F-03: NaN and Infinity are rejected without modifying state.
///   - G-01: Restore restores ONLY the accumulated total; the caller MUST
///     call StartActivePeriod before UpdateTo. Offline time is NOT counted.
///   - G-01: The accumulated total is session-level, not per-day reset.
/// </summary>
[TestClass]
public class EffectiveRuntimeTrackerTests
{
    // Monotonic time in minutes — simulates Stopwatch.GetTimestamp() converted to minutes.
    // Unlike UTC, this value always moves forward regardless of system clock changes.
    private const double T0 = 0.0;

    [TestMethod]
    public void UpdateTo_AfterStart_AccumulatesElapsed()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);

        var minutes = tracker.UpdateTo(30.0);

        Assert.AreEqual(30.0, minutes, 0.001);
    }

    [TestMethod]
    public void UpdateTo_MultipleTicks_AccumulatesCorrectly()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);

        tracker.UpdateTo(10.0);
        tracker.UpdateTo(20.0);
        var minutes = tracker.UpdateTo(35.0);

        Assert.AreEqual(35.0, minutes, 0.001);
    }

    [TestMethod]
    public void EndActivePeriod_PauseExcludesTime()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);

        // Active for 20 minutes, then pause.
        tracker.EndActivePeriod(20.0);
        Assert.AreEqual(20.0, tracker.AccumulatedMinutes, 0.001);

        // Paused for 60 minutes — should not accumulate.
        // (No UpdateTo calls during pause.)

        // Resume at monotonic time 80.
        tracker.StartActivePeriod(80.0);
        var minutes = tracker.UpdateTo(90.0);

        // Total: 20 (before pause) + 10 (after resume) = 30.
        Assert.AreEqual(30.0, minutes, 0.001);
    }

    // ── F-02: Monotonic clock — UTC wall clock changes do not affect runtime ──

    [TestMethod]
    public void UpdateTo_MonotonicClockAlwaysAdvances_UtcJumpIrrelevant()
    {
        // Simulate: system UTC jumps forward 12 hours, but monotonic clock
        // only advances 5 minutes. Only 5 minutes of runtime should accumulate.
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(100.0); // monotonic start

        // Monotonic advances 5 minutes (even though UTC jumped 720 minutes).
        var minutes = tracker.UpdateTo(105.0);

        Assert.AreEqual(5.0, minutes, 0.001);
    }

    [TestMethod]
    public void UpdateTo_MonotonicClockDoesNotGoBackward()
    {
        // Monotonic clock should never go backward, but if a counter reset
        // happens (e.g. overflow), treat as zero elapsed.
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(50.0);
        tracker.UpdateTo(60.0); // 10 min accumulated.

        // Counter "resets" to a lower value — no negative accumulation.
        var minutes = tracker.UpdateTo(5.0);

        Assert.AreEqual(10.0, minutes, 0.001);
        // Reference should not move backward.
        Assert.AreEqual(60.0, tracker.LastActiveMonotonicMinutes!.Value, 0.001);
    }

    // ── F-03: Invalid inputs rejected ──────────────────────────────────────

    [TestMethod]
    public void UpdateTo_NaN_RejectedWithoutStateChange()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(10.0);

        var minutes = tracker.UpdateTo(double.NaN);

        Assert.AreEqual(10.0, minutes, 0.001);
        Assert.AreEqual(10.0, tracker.AccumulatedMinutes, 0.001);
    }

    [TestMethod]
    public void UpdateTo_Infinity_RejectedWithoutStateChange()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(10.0);

        var minutes = tracker.UpdateTo(double.PositiveInfinity);

        Assert.AreEqual(10.0, minutes, 0.001);
    }

    [TestMethod]
    public void UpdateTo_NegativeInfinity_RejectedWithoutStateChange()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(10.0);

        var minutes = tracker.UpdateTo(double.NegativeInfinity);

        Assert.AreEqual(10.0, minutes, 0.001);
    }

    [TestMethod]
    public void Restore_NaN_ClampedToZero()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.Restore(double.NaN);

        Assert.AreEqual(0.0, tracker.AccumulatedMinutes, 0.001);
        Assert.IsNull(tracker.LastActiveMonotonicMinutes);
    }

    [TestMethod]
    public void Restore_Infinity_ClampedToZero()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.Restore(double.PositiveInfinity);

        Assert.AreEqual(0.0, tracker.AccumulatedMinutes, 0.001);
    }

    [TestMethod]
    public void Restore_Negative_ClampedToZero()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.Restore(-100.0);

        Assert.AreEqual(0.0, tracker.AccumulatedMinutes, 0.001);
    }

    [TestMethod]
    public void Restore_ValidValue_Accepted()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.Restore(45.5);

        Assert.AreEqual(45.5, tracker.AccumulatedMinutes, 0.001);
        Assert.IsNull(tracker.LastActiveMonotonicMinutes);
    }

    // ── G-01: Offline time contract ─────────────────────────────────────────

    [TestMethod]
    public void Restore_ThenStartActivePeriod_OfflineTimeNotCounted()
    {
        // Simulate: app was running for 45 min, then closed for 12 hours, then restarted.
        // On restart, Restore(45) sets accumulated=45.
        // The caller MUST call StartActivePeriod with the CURRENT monotonic time.
        // The 12 hours offline should NOT be added.
        var tracker = new EffectiveRuntimeTracker();
        tracker.Restore(45.0);

        // After restart, start a new active period at monotonic time 1000.
        tracker.StartActivePeriod(1000.0);

        // Accumulate 15 minutes of new active time.
        var minutes = tracker.UpdateTo(1015.0);

        // Total: 45 (restored) + 15 (new active) = 60. NOT 45 + 1015 = 1060.
        Assert.AreEqual(60.0, minutes, 0.001);
    }

    [TestMethod]
    public void Restore_WithoutStartActivePeriod_UpdateToReturnsAccumulatedOnly()
    {
        // If caller forgets to call StartActivePeriod after Restore,
        // UpdateTo should return only the restored accumulated value,
        // NOT add the time since Restore.
        var tracker = new EffectiveRuntimeTracker();
        tracker.Restore(30.0);

        // No StartActivePeriod call — still "paused".
        var minutes = tracker.UpdateTo(500.0);

        Assert.AreEqual(30.0, minutes, 0.001);
    }

    [TestMethod]
    public void Restore_ThenStartAndPause_OnlyActiveTimeAdded()
    {
        // Full lifecycle: restore → start → active 10 min → pause 60 min → resume → active 5 min.
        var tracker = new EffectiveRuntimeTracker();
        tracker.Restore(20.0);
        tracker.StartActivePeriod(100.0);

        tracker.UpdateTo(110.0); // +10 min active.
        tracker.EndActivePeriod(110.0); // Pause.

        // Paused for 60 min (no UpdateTo calls during pause).

        tracker.StartActivePeriod(170.0); // Resume.
        var minutes = tracker.UpdateTo(175.0); // +5 min active.

        // Total: 20 (restored) + 10 + 5 = 35. Pause not counted.
        Assert.AreEqual(35.0, minutes, 0.001);
    }

    // ── G-01: Session-level accumulation, not per-day reset ─────────────────

    [TestMethod]
    public void AccumulatedMinutes_AreSessionLevel_NotPerDayReset()
    {
        // The accumulated minutes represent total effective runtime since the
        // pet started running. A new day does NOT reset the accumulator —
        // the scheduler creates a new plan with its own delay, but the
        // accumulated minutes continue.
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(50.0); // 50 min of runtime on "day 1".

        // "Day 2" — runtime continues, does not reset.
        var minutes = tracker.UpdateTo(75.0);

        Assert.AreEqual(75.0, minutes, 0.001);
    }

    // ── Pause / resume edge cases ───────────────────────────────────────────

    [TestMethod]
    public void EndActivePeriod_WithoutStart_DoesNothing()
    {
        var tracker = new EffectiveRuntimeTracker();

        tracker.EndActivePeriod(10.0);

        Assert.AreEqual(0.0, tracker.AccumulatedMinutes, 0.001);
        Assert.IsNull(tracker.LastActiveMonotonicMinutes);
    }

    [TestMethod]
    public void UpdateTo_WhenPaused_ReturnsAccumulatedOnly()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(15.0);

        // Pause.
        tracker.EndActivePeriod(15.0);

        // UpdateTo while paused: returns accumulated.
        var minutes = tracker.UpdateTo(100.0);

        Assert.AreEqual(15.0, minutes, 0.001);
    }

    [TestMethod]
    public void StartActivePeriod_ResetsReference()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(10.0);

        // Start a new active period at a later time.
        tracker.StartActivePeriod(50.0);

        var minutes = tracker.UpdateTo(55.0);

        // Previous 10 + new 5 = 15.
        Assert.AreEqual(15.0, minutes, 0.001);
    }

    [TestMethod]
    public void UpdateTo_FractionalMinutes_AccumulateCorrectly()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);

        tracker.UpdateTo(0.5);   // 0.5 min
        tracker.UpdateTo(1.5);   // 1.0 min more
        var minutes = tracker.UpdateTo(2.5); // 1.0 min more

        Assert.AreEqual(2.5, minutes, 0.001);
    }

    // ── F-03 R1: Start/End invalid inputs in active state ──────────────────

    [TestMethod]
    public void F03_StartActivePeriod_NaN_RejectedWithoutStateChange()
    {
        // Active state with 10 min accumulated, then StartActivePeriod(NaN) must
        // NOT replace the existing valid reference.
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(10.0);

        tracker.StartActivePeriod(double.NaN);

        // Reference should still be 10.0 (the last UpdateTo value), not NaN.
        Assert.AreEqual(10.0, tracker.LastActiveMonotonicMinutes!.Value, 0.001);
        // Subsequent UpdateTo should continue accumulating normally.
        var minutes = tracker.UpdateTo(15.0);
        Assert.AreEqual(15.0, minutes, 0.001);
    }

    [TestMethod]
    public void F03_StartActivePeriod_Infinity_RejectedWithoutStateChange()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(10.0);

        tracker.StartActivePeriod(double.PositiveInfinity);

        Assert.AreEqual(10.0, tracker.LastActiveMonotonicMinutes!.Value, 0.001);
        var minutes = tracker.UpdateTo(20.0);
        Assert.AreEqual(20.0, minutes, 0.001);
    }

    [TestMethod]
    public void F03_StartActivePeriod_Negative_RejectedWithoutStateChange()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(10.0);

        tracker.StartActivePeriod(-5.0);

        Assert.AreEqual(10.0, tracker.LastActiveMonotonicMinutes!.Value, 0.001);
        var minutes = tracker.UpdateTo(12.0);
        Assert.AreEqual(12.0, minutes, 0.001);
    }

    [TestMethod]
    public void F03_EndActivePeriod_NaN_RejectedWithoutStateChange()
    {
        // Active state, EndActivePeriod(NaN) must NOT end the active period or
        // modify accumulated minutes.
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(10.0);

        tracker.EndActivePeriod(double.NaN);

        // Active period should still be active (reference not cleared).
        Assert.IsNotNull(tracker.LastActiveMonotonicMinutes);
        Assert.AreEqual(10.0, tracker.AccumulatedMinutes, 0.001);
        // Subsequent UpdateTo should continue accumulating.
        var minutes = tracker.UpdateTo(15.0);
        Assert.AreEqual(15.0, minutes, 0.001);
    }

    [TestMethod]
    public void F03_EndActivePeriod_Infinity_RejectedWithoutStateChange()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(10.0);

        tracker.EndActivePeriod(double.PositiveInfinity);

        // Must NOT have written Infinity to accumulated minutes.
        Assert.IsFalse(double.IsInfinity(tracker.AccumulatedMinutes));
        Assert.IsNotNull(tracker.LastActiveMonotonicMinutes);
        Assert.AreEqual(10.0, tracker.AccumulatedMinutes, 0.001);
    }

    [TestMethod]
    public void F03_EndActivePeriod_Negative_RejectedWithoutStateChange()
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(T0);
        tracker.UpdateTo(10.0);

        tracker.EndActivePeriod(-5.0);

        Assert.IsNotNull(tracker.LastActiveMonotonicMinutes);
        Assert.AreEqual(10.0, tracker.AccumulatedMinutes, 0.001);
    }

    [TestMethod]
    public void F03_StartEnd_InvalidThenNormal_ContinuesCorrectly()
    {
        // Full lifecycle: valid start → accumulate → invalid start (rejected) →
        // continue accumulate → invalid end (rejected) → valid end → resume → accumulate.
        var tracker = new EffectiveRuntimeTracker();
        tracker.StartActivePeriod(100.0);
        tracker.UpdateTo(110.0); // 10 min

        // Invalid start should be rejected; reference stays at 110.
        tracker.StartActivePeriod(double.NaN);
        Assert.AreEqual(110.0, tracker.LastActiveMonotonicMinutes!.Value, 0.001);

        // Continue accumulating.
        tracker.UpdateTo(120.0); // +10 min, total 20
        Assert.AreEqual(20.0, tracker.AccumulatedMinutes, 0.001);

        // Invalid end should be rejected; active period continues.
        tracker.EndActivePeriod(double.PositiveInfinity);
        Assert.IsNotNull(tracker.LastActiveMonotonicMinutes);

        // Valid end at 130: +10 min, total 30.
        tracker.EndActivePeriod(130.0);
        Assert.AreEqual(30.0, tracker.AccumulatedMinutes, 0.001);
        Assert.IsNull(tracker.LastActiveMonotonicMinutes);

        // Resume and accumulate.
        tracker.StartActivePeriod(200.0);
        var minutes = tracker.UpdateTo(205.0);
        Assert.AreEqual(35.0, minutes, 0.001);
    }
}
