using DesktopPet.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// P4-001 M-11 §1: Core假时钟测试 for <see cref="ActivitySession"/>.
/// <para>
/// Verifies R-06 requirements:
/// - Active window (60s): input within window → active, beyond → idle.
/// - Idle reset (5 min): continuous idle ≥ 300s → session resets.
/// - Brief reading pause: idle &lt; 5 min → no accumulation, but session
///   not reset; resuming input continues accumulation.
/// - Threshold (2h = 120 min): accumulated active ≥ 120 min → fire once.
/// - Same session does not re-fire.
/// - After reset, a new session can fire again.
/// - Suspend (lock/sleep/pause): stops accumulating; resume from new
///   baseline; suspended interval discarded; no reminder replay.
/// - Invalid inputs rejected without state change.
/// - Long intervals, pause and lock/sleep overlap: correct timing.
/// </para>
/// </summary>
[TestClass]
public class ActivitySessionTests
{
    // All times in milliseconds.
    private const double MsPerSec = 1000.0;
    private const double MsPerMin = 60000.0;
    private const double ActiveWindow = 60.0 * MsPerSec;   // 60s
    private const double IdleReset = 300.0 * MsPerSec;     // 5 min
    private const double Threshold = 120.0 * MsPerMin;     // 2h

    [TestMethod]
    public void Active_WithinWindow_Accumulates()
    {
        var session = new ActivitySession();
        // User input at t=0, tick at t=1000ms (1s later, within 60s window).
        session.Tick(0, 0);
        session.Tick(1000, 0);

        // 1 second of active time accumulated.
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1);
    }

    [TestMethod]
    public void Active_MultipleTicks_AccumulateCorrectly()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals (under MaxSampleGapMs=5s)
        // to simulate 10 minutes of continuous activity.
        session.Tick(0, 0);
        for (int i = 1; i <= 600; i++)
        {
            session.Tick(i * MsPerSec, i * MsPerSec); // input = now (always active)
        }

        Assert.AreEqual(10.0, session.AccumulatedActiveMinutes, 0.001);
    }

    [TestMethod]
    public void Idle_BeyondActiveWindow_StopsAccumulation()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals. Active for 5 minutes.
        session.Tick(0, 0);
        for (int i = 1; i <= 300; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);

        Assert.AreEqual(5.0, session.AccumulatedActiveMinutes, 0.001);

        // User goes idle: last input at 5 min, tick at 5min + 90s (beyond 60s window).
        var idleTick = 300 * MsPerSec + 90 * MsPerSec;
        session.Tick(idleTick, 300 * MsPerSec);

        // No additional accumulation.
        Assert.AreEqual(5.0, session.AccumulatedActiveMinutes, 0.001);
    }

    [TestMethod]
    public void BriefReadingPause_ThenResume_ContinuesAccumulation()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals. Active for 10 minutes.
        session.Tick(0, 0);
        for (int i = 1; i <= 600; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);

        // User reads for 3 minutes (idle but < 5 min reset).
        // The first 60 seconds after last input are still "active" per
        // the active window. Idle ticks start at 61s to be truly idle.
        var idleStart = 600 * MsPerSec;
        session.Tick(idleStart + 61 * MsPerSec, idleStart);
        // Now idle. Tick through 3 min of idle (61s to 180s).
        for (int s = 62; s <= 180; s++)
        {
            var t = idleStart + s * MsPerSec;
            session.Tick(t, idleStart); // last input at 10 min
        }

        // Still 10 min — no accumulation during idle.
        Assert.AreEqual(10.0, session.AccumulatedActiveMinutes, 0.001);
        Assert.IsFalse(session.ReminderFired);

        // User resumes input at 13 min.
        session.Tick(780 * MsPerSec, 780 * MsPerSec);
        session.Tick(781 * MsPerSec, 781 * MsPerSec);

        // 10 + 1s = ~10.017 min (the tick at 780s sets reference, 781s adds 1s).
        Assert.AreEqual(10.0 + 1.0 / 60.0, session.AccumulatedActiveMinutes, 0.001);
    }

    [TestMethod]
    public void IdleFiveMinutes_ResetsSession()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals. Active for 30 minutes.
        session.Tick(0, 0);
        for (int i = 1; i <= 1800; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);

        Assert.AreEqual(30.0, session.AccumulatedActiveMinutes, 0.001);

        // User goes idle for 5 minutes continuously.
        // F-01 R2: Use 1-second tick intervals through the idle period
        // (each tick must be under MaxSampleGapMs=5s).
        var idleStart = 1800 * MsPerSec;
        // First idle tick at 61s (beyond active window).
        session.Tick(idleStart + 61 * MsPerSec, idleStart);
        // Continue idle ticks every 1s until 5 min → reset.
        for (int s = 62; s <= 300; s++)
            session.Tick(idleStart + s * MsPerSec, idleStart);

        // Session reset.
        Assert.AreEqual(0.0, session.AccumulatedActiveMinutes, 0.001);
        Assert.IsFalse(session.ReminderFired);
    }

    [TestMethod]
    public void ThresholdReached_FiresOncePerSession()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);

        bool fired = false;
        // F-01 R2: Use 1-second tick intervals. Accumulate exactly 120 minutes.
        for (int i = 1; i <= 7200; i++)
        {
            var t = i * MsPerSec;
            if (session.Tick(t, t))
                fired = true;
        }

        Assert.IsTrue(fired);
        Assert.IsTrue(session.ReminderFired);
        Assert.AreEqual(120.0, session.AccumulatedActiveMinutes, 0.001);

        // Continue accumulating — should NOT fire again.
        fired = false;
        for (int i = 7201; i <= 7800; i++)
        {
            var t = i * MsPerSec;
            if (session.Tick(t, t))
                fired = true;
        }

        Assert.IsFalse(fired);
        Assert.IsTrue(session.ReminderFired);
    }

    [TestMethod]
    public void AfterIdleReset_CanFireAgain()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);

        // F-01 R2: Use 1-second tick intervals. Accumulate 120 min → fire.
        bool fired = false;
        for (int i = 1; i <= 7200; i++)
        {
            var t = i * MsPerSec;
            if (session.Tick(t, t))
                fired = true;
        }
        Assert.IsTrue(fired);

        // Idle 5 min → reset. F-01 R2: Use 1-second idle ticks.
        var idleStart = 7200 * MsPerSec;
        session.Tick(idleStart + 61 * MsPerSec, idleStart);
        for (int s = 62; s <= 300; s++)
            session.Tick(idleStart + s * MsPerSec, idleStart);
        Assert.IsFalse(session.ReminderFired);
        Assert.AreEqual(0.0, session.AccumulatedActiveMinutes, 0.001);

        // Accumulate another 120 min → fire again.
        fired = false;
        var resumeBase = idleStart + IdleReset;
        session.Tick(resumeBase, resumeBase);
        for (int i = 1; i <= 7200; i++)
        {
            var t = resumeBase + i * MsPerSec;
            if (session.Tick(t, t))
                fired = true;
        }
        Assert.IsTrue(fired);
    }

    [TestMethod]
    public void Suspend_StopsAccumulation()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals.
        session.Tick(0, 0);
        for (int i = 1; i <= 600; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);

        Assert.AreEqual(10.0, session.AccumulatedActiveMinutes, 0.001);

        // Suspend (lock screen).
        session.Suspend();

        // Tick during suspension — no-op.
        var fired = session.Tick(6000 * MsPerSec, 6000 * MsPerSec);
        Assert.IsFalse(fired);
        Assert.AreEqual(10.0, session.AccumulatedActiveMinutes, 0.001);
        Assert.IsTrue(session.IsSuspended);
    }

    [TestMethod]
    public void Resume_FromNewBaseline_NoBackfill()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals.
        session.Tick(0, 0);
        for (int i = 1; i <= 600; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);

        // Suspend at 10 min.
        session.Suspend();

        // 50 minutes pass while suspended (lock/sleep).
        // Resume at 60 min.
        session.Resume(3600 * MsPerSec);

        // The 50 min gap is NOT backfilled.
        Assert.AreEqual(10.0, session.AccumulatedActiveMinutes, 0.001);
        Assert.IsFalse(session.IsSuspended);

        // Accumulate 5 more minutes (1-second ticks).
        session.Tick(3600 * MsPerSec, 3600 * MsPerSec); // sets reference
        for (int i = 3601; i <= 3900; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);

        Assert.AreEqual(15.0, session.AccumulatedActiveMinutes, 0.001);
    }

    [TestMethod]
    public void Resume_PreservesAccumulatedAndReminderState()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);

        // F-01 R2: Use 1-second tick intervals. Accumulate 100 min.
        for (int i = 1; i <= 6000; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(100.0, session.AccumulatedActiveMinutes, 0.001);

        // Suspend and resume.
        session.Suspend();
        session.Resume(12000 * MsPerSec);

        // Accumulated preserved.
        Assert.AreEqual(100.0, session.AccumulatedActiveMinutes, 0.001);
        Assert.IsFalse(session.ReminderFired);

        // Continue to threshold → fire.
        bool fired = false;
        session.Tick(12000 * MsPerSec, 12000 * MsPerSec);
        for (int i = 1; i <= 1200; i++)
        {
            var t = 12000 * MsPerSec + i * MsPerSec;
            if (session.Tick(t, t))
                fired = true;
        }
        Assert.IsTrue(fired);
    }

    [TestMethod]
    public void SuspendDuringIdle_DiscardsIdleProgress()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals.
        session.Tick(0, 0);
        for (int i = 1; i <= 600; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);

        // User goes idle. First 60s are still "active" per the window.
        // Tick at 61s to enter true idle, then suspend.
        var idleStart = 600 * MsPerSec;
        session.Tick(idleStart + 61 * MsPerSec, idleStart);

        // Suspend (lock) during the idle period.
        session.Suspend();

        // 2 hours pass while suspended.
        session.Resume(7800 * MsPerSec);

        // The partial idle is discarded. The session should NOT reset
        // (it didn't reach 5 min continuous idle). Accumulated stays at
        // 10 min — the tick at 61s was idle, so no time from 10min→10min61s
        // is accumulated (conservative: tick-level granularity).
        var accumulated = session.AccumulatedActiveMinutes;
        Assert.AreEqual(10.0, accumulated, 0.001);
        Assert.IsFalse(session.ReminderFired);

        // User is active again — should accumulate normally.
        session.Tick(7800 * MsPerSec, 7800 * MsPerSec);
        session.Tick(7801 * MsPerSec, 7801 * MsPerSec);
        Assert.AreEqual(10.0 + 1.0 / 60.0, session.AccumulatedActiveMinutes, 0.001);
    }

    [TestMethod]
    public void PauseAndLockOverlap_BothSuspendCorrectly()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals.
        session.Tick(0, 0);
        for (int i = 1; i <= 1200; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);

        // Pause (app-level).
        session.Suspend();
        Assert.IsTrue(session.IsSuspended);

        // Tick during pause — no-op.
        session.Tick(6000 * MsPerSec, 6000 * MsPerSec);
        Assert.AreEqual(20.0, session.AccumulatedActiveMinutes, 0.001);

        // Resume from pause.
        session.Resume(6000 * MsPerSec);
        Assert.IsFalse(session.IsSuspended);

        // Immediately lock (system-level).
        session.Suspend();
        Assert.IsTrue(session.IsSuspended);

        // Tick during lock — no-op.
        session.Tick(12000 * MsPerSec, 12000 * MsPerSec);
        Assert.AreEqual(20.0, session.AccumulatedActiveMinutes, 0.001);

        // Unlock.
        session.Resume(12000 * MsPerSec);
        session.Tick(12000 * MsPerSec, 12000 * MsPerSec);
        for (int i = 12001; i <= 12300; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(25.0, session.AccumulatedActiveMinutes, 0.001);
    }

    [TestMethod]
    public void NoInputEver_TreatedAsIdle()
    {
        var session = new ActivitySession();
        // F-01 R1: No input ever (lastInputMs = null) — the input age
        // is unknown, so we rebase without accumulating. This is NOT
        // treated as idle (which would eventually reset); it's a
        // missing sample that simply doesn't accumulate.
        session.Tick(0, null);
        session.Tick(60 * MsPerSec, null);

        // No accumulation.
        Assert.AreEqual(0.0, session.AccumulatedActiveMs, 0.1);
    }

    [TestMethod]
    public void InvalidNowMs_RejectedWithoutStateChange()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);
        session.Tick(60 * MsPerSec, 60 * MsPerSec);

        var before = session.AccumulatedActiveMs;
        session.Tick(double.NaN, 0);
        session.Tick(double.PositiveInfinity, 0);
        session.Tick(-1, 0);

        Assert.AreEqual(before, session.AccumulatedActiveMs, 0.1);
    }

    [TestMethod]
    public void InvalidLastInputMs_TreatedAsMissingSample()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);
        session.Tick(1000, 1000);

        // F-01 R1: Invalid lastInputMs (NaN) — treated as a missing
        // sample, not idle. The session rebases without accumulating
        // and without entering the idle-reset path.
        session.Tick(2000, double.NaN);
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1);

        // F-01 R3: After the missing sample, the first valid active tick
        // only re-establishes the baseline (does NOT accumulate the gap
        // from the missing sample to now). Accumulated stays at 1000.
        session.Tick(3000, 3000);
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1);

        // The next valid active tick accumulates normally from the baseline.
        session.Tick(4000, 4000);
        Assert.AreEqual(2000.0, session.AccumulatedActiveMs, 0.1);
    }

    [TestMethod]
    public void LongInterval_ActiveThenIdle_CorrectTiming()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals. Active at t=0.
        session.Tick(0, 0);

        // Accumulate 5 minutes with 1-second ticks.
        for (int i = 1; i <= 300; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(5.0, session.AccumulatedActiveMinutes, 0.001);

        // Next tick at t=5min+61s, input still at t=5min (idle, > 60s).
        session.Tick(300 * MsPerSec + 61 * MsPerSec, 300 * MsPerSec);
        Assert.AreEqual(5.0, session.AccumulatedActiveMinutes, 0.001);

        // Continue idle with 1-second ticks to 5 min → reset.
        for (int s = 62; s <= 300; s++)
            session.Tick(300 * MsPerSec + s * MsPerSec, 300 * MsPerSec);
        Assert.AreEqual(0.0, session.AccumulatedActiveMinutes, 0.001);
    }

    [TestMethod]
    public void Reset_ClearsAccumulatedAndReminder()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals.
        session.Tick(0, 0);
        for (int i = 1; i <= 7200; i++)
        {
            var t = i * MsPerSec;
            session.Tick(t, t);
        }
        Assert.IsTrue(session.ReminderFired);

        session.Reset();
        Assert.AreEqual(0.0, session.AccumulatedActiveMs, 0.1);
        Assert.IsFalse(session.ReminderFired);
    }

    [TestMethod]
    public void Resume_InvalidNowMs_Rejected()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);
        session.Suspend();

        session.Resume(double.NaN);
        Assert.IsTrue(session.IsSuspended);

        session.Resume(double.PositiveInfinity);
        Assert.IsTrue(session.IsSuspended);

        session.Resume(-1);
        Assert.IsTrue(session.IsSuspended);

        // Valid resume works.
        session.Resume(100 * MsPerSec);
        Assert.IsFalse(session.IsSuspended);
    }

    [TestMethod]
    public void Tick_WhileSuspended_ReturnsFalse()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals.
        session.Tick(0, 0);
        for (int i = 1; i <= 7800; i++)
        {
            var t = i * MsPerSec;
            session.Tick(t, t);
        }

        // Should have fired.
        Assert.IsTrue(session.ReminderFired);

        // Suspend and tick — no fire.
        session.Suspend();
        var fired = session.Tick(60000 * MsPerSec, 60000 * MsPerSec);
        Assert.IsFalse(fired);
    }

    [TestMethod]
    public void ThresholdExactly120_Fires()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);

        // F-01 R2: Use 1-second tick intervals. Accumulate to exactly 119 min.
        for (int i = 1; i <= 7140; i++)
        {
            var t = i * MsPerSec;
            var fired = session.Tick(t, t);
            Assert.IsFalse(fired, $"Should not fire at {i} s");
        }

        // Continue 1-second ticks to 120th minute (7200th second) → fire.
        bool firedAt120 = false;
        for (int i = 7141; i <= 7200; i++)
        {
            var t = i * MsPerSec;
            if (session.Tick(t, t))
                firedAt120 = true;
        }
        Assert.IsTrue(firedAt120);
    }

    [TestMethod]
    public void FractionalActiveTime_AccumulatesCorrectly()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);

        // F-01 R2: Use 1-second intervals (under MaxSampleGapMs=5s).
        // 4 ticks = 4 seconds.
        session.Tick(1000, 1000);
        session.Tick(2000, 2000);
        session.Tick(3000, 3000);
        session.Tick(4000, 4000);

        Assert.AreEqual(4.0 / 60.0, session.AccumulatedActiveMinutes, 0.001);
    }

    // === F-01 R1: Long interval, missing sample, future timestamp ===

    /// <summary>
    /// F-01 R1: A long unobserved gap between ticks must not be
    /// backfilled, even if the user has fresh input at the end.
    /// The session rebases to the current tick without accumulating
    /// the gap.
    /// </summary>
    [TestMethod]
    public void LongGap_WithFreshInput_DoesNotBackfill()
    {
        var session = new ActivitySession();
        // 1 second of active time.
        session.Tick(0, 0);
        session.Tick(1000, 1000);
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1);

        // 2 hours pass (unobserved gap), then fresh input at the end.
        // F-01 R2: The gap exceeds MaxSampleGapMs (5 s), so it's discarded.
        var fired = session.Tick(7_201_000, 7_201_000);
        Assert.IsFalse(fired, "A fresh input after a long unobserved gap must not issue a reminder.");
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1,
            "Accumulated value must not change after a long gap.");
    }

    /// <summary>
    /// F-01 R1: A missing input sample (null) must not be treated as
    /// "user has rested 5 minutes". The session rebases without
    /// accumulating and without entering the idle-reset path.
    /// Previously accumulated value is preserved.
    /// </summary>
    [TestMethod]
    public void MissingInputSample_DoesNotResetAccumulated()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);
        session.Tick(1000, 1000);
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1);

        // Missing sample (null) — should NOT clear accumulated value.
        session.Tick(302000, null);
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1,
            "Missing input sample must not fabricate idle time or clear accumulated value.");

        // Even after a long time with null samples, accumulated is preserved.
        session.Tick(602000, null);
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1);
    }

    /// <summary>
    /// F-01 R1: A future timestamp (lastInputMs > nowMs) must not be
    /// treated as recent activity. The session treats it as a missing
    /// sample and rebases without accumulating.
    /// </summary>
    [TestMethod]
    public void FutureTimestamp_NotTreatedAsActive()
    {
        var session = new ActivitySession();
        // First tick with future input — treated as missing sample.
        session.Tick(1000, 2000);
        Assert.AreEqual(0.0, session.AccumulatedActiveMs, 0.1);

        // Second tick with same future input — still no accumulation.
        session.Tick(1500, 2000);
        Assert.AreEqual(0.0, session.AccumulatedActiveMs, 0.1);
    }

    /// <summary>
    /// F-01 R1/R3: After a missing sample, the first valid active tick
    /// only re-establishes the baseline (does NOT accumulate the gap).
    /// The second valid tick accumulates normally from the baseline.
    /// </summary>
    [TestMethod]
    public void AfterMissingSample_ValidActiveTick_Accumulates()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);
        session.Tick(1000, 1000);
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1);

        // Missing sample rebases to 302000.
        session.Tick(302000, null);
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1);

        // F-01 R3: First valid active tick — only establishes baseline,
        // does NOT accumulate the gap from the missing sample.
        session.Tick(303000, 303000);
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1);

        // Second valid active tick — accumulates normally from baseline.
        session.Tick(304000, 304000);
        Assert.AreEqual(2000.0, session.AccumulatedActiveMs, 0.1);
    }

    /// <summary>
    /// F-01 R2: A gap just under MaxSampleGapMs (5 s) is still
    /// accumulated normally (not discarded).
    /// </summary>
    [TestMethod]
    public void GapUnderMaxSampleGap_AccumulatesNormally()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);

        // 4 seconds gap — under MaxSampleGapMs (5 s), should accumulate.
        session.Tick(4000, 4000);
        Assert.AreEqual(4.0 / 60.0, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// F-01 R2: A gap exactly at MaxSampleGapMs (5 s) is discarded.
    /// </summary>
    [TestMethod]
    public void GapAtMaxSampleGap_Discarded()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);
        session.Tick(1000, 1000);
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1);

        // Gap exactly at MaxSampleGapMs (5000 ms) — 1000 + 5000 = 6000.
        session.Tick(6000, 6000);
        Assert.AreEqual(1000.0, session.AccumulatedActiveMs, 0.1,
            "Gap at MaxSampleGapMs should be discarded, not accumulated.");
    }

    /// <summary>
    /// F-01 R1: Valid idle (with real input data) for 5 minutes still
    /// resets the session. This verifies the idle-reset path is not
    /// broken by the F-01 changes.
    /// </summary>
    [TestMethod]
    public void ValidIdle_FiveMinutes_ResetsSession()
    {
        var session = new ActivitySession();
        // F-01 R2: Use 1-second tick intervals.
        session.Tick(0, 0);
        for (int i = 1; i <= 1800; i++)
            session.Tick(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(30.0, session.AccumulatedActiveMinutes, 0.001);

        // User goes idle with valid input data.
        // F-01 R2: Use 1-second tick intervals through the idle period.
        var idleStart = 1800 * MsPerSec;
        session.Tick(idleStart + 61 * MsPerSec, idleStart); // idle
        for (int s = 62; s <= 300; s++)
            session.Tick(idleStart + s * MsPerSec, idleStart); // 5 min idle → reset

        Assert.AreEqual(0.0, session.AccumulatedActiveMinutes, 0.001);
        Assert.IsFalse(session.ReminderFired);
    }
}
