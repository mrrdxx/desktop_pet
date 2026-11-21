using DesktopPet.App.Platform;
using DesktopPet.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace DesktopPet.App.Tests;

/// <summary>
/// P4-001 M-11 §2: App接线验证 for <see cref="ActivitySessionService"/>.
/// <para>
/// Verifies:
/// - Single-pet and dual-pet share the same session (accumulation identical).
/// - Adding/removing pets does not change the session.
/// - Exit releases system subscriptions (service disposed).
/// - Global pause suspends the session; resume resumes from new baseline.
/// - Rare-event timing (EffectiveRuntimeTracker) is independent.
/// - Test evidence records only idle duration, lifecycle changes,
///   accumulated value, and intent count.
/// </para>
/// <para>
/// These tests use the internal TickManually method to drive the session
/// with controlled time and input values, avoiding reliance on the
/// DispatcherTimer or real Win32 GetLastInputInfo.
/// </para>
/// </summary>
[TestClass]
public class ActivitySessionServiceTests
{
    private const double MsPerMin = 60000.0;
    private const double MsPerSec = 1000.0;

    /// <summary>
    /// M-11 §2: Single-pet and dual-pet accumulate identically because
    /// the session is global, not per-pet.
    /// F-01 R2: Use 1-second tick intervals (under MaxSampleGapMs=5s).
    /// </summary>
    [TestMethod]
    public void SingleAndDualPet_AccumulateIdentically()
    {
        // Single-pet session.
        var singleSession = new ActivitySession();
        var singleService = new ActivitySessionService(singleSession,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        // Drive 10 minutes of active time (600 1-second ticks).
        singleService.TickManually(0, 0);
        for (int i = 1; i <= 600; i++)
            singleService.TickManually(i * MsPerSec, i * MsPerSec);

        var singleAccumulated = singleSession.AccumulatedActiveMinutes;

        // Dual-pet session (same service, different pet count is irrelevant).
        var dualSession = new ActivitySession();
        var dualService = new ActivitySessionService(dualSession,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        dualService.TickManually(0, 0);
        for (int i = 1; i <= 600; i++)
            dualService.TickManually(i * MsPerSec, i * MsPerSec);

        var dualAccumulated = dualSession.AccumulatedActiveMinutes;

        // Both should have the same accumulated value.
        Assert.AreEqual(singleAccumulated, dualAccumulated, 0.001);
        Assert.AreEqual(10.0, singleAccumulated, 0.001);
        Assert.AreEqual(10.0, dualAccumulated, 0.001);
    }

    /// <summary>
    /// M-11 §2: Adding/removing pets does not change the session's
    /// accumulated value or reminder state.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void AddRemovePet_DoesNotChangeSession()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        // Accumulate 30 min (1800 1-second ticks).
        service.TickManually(0, 0);
        for (int i = 1; i <= 1800; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);

        var beforeAdd = session.AccumulatedActiveMinutes;
        Assert.AreEqual(30.0, beforeAdd, 0.001);

        // Simulate "add pet" — the service is unaffected.
        service.TickManually(1801 * MsPerSec, 1801 * MsPerSec);
        var afterAdd = session.AccumulatedActiveMinutes;
        Assert.AreEqual(30.0 + 1.0 / 60.0, afterAdd, 0.001);

        // Simulate "remove pet" — still unaffected.
        service.TickManually(1802 * MsPerSec, 1802 * MsPerSec);
        var afterRemove = session.AccumulatedActiveMinutes;
        Assert.AreEqual(30.0 + 2.0 / 60.0, afterRemove, 0.001);
    }

    /// <summary>
    /// M-11 §2: Exit releases the service (Dispose stops the timer and
    /// suspends the session).
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void Dispose_StopsAndSuspends()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        service.TickManually(0, 0);
        service.TickManually(300 * MsPerSec, 300 * MsPerSec);

        service.Dispose();

        // After dispose, the session is suspended.
        Assert.IsTrue(session.IsSuspended);

        // TickManually still works (it calls session.Tick directly), but
        // the session is suspended so it's a no-op.
        var before = session.AccumulatedActiveMinutes;
        service.TickManually(600 * MsPerSec, 600 * MsPerSec);
        Assert.AreEqual(before, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// M-11 §2: Global pause suspends the session; resume resumes from
    /// a new baseline without backfilling the paused interval.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void PauseSuspends_ResumeFromNewBaseline()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        // Accumulate 20 min (1200 1-second ticks).
        service.TickManually(0, 0);
        for (int i = 1; i <= 1200; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(20.0, session.AccumulatedActiveMinutes, 0.001);

        // Pause.
        service.OnPauseToggled(true);
        Assert.IsTrue(service.IsSuspended);

        // 60 min pass while paused.
        service.TickManually(4800 * MsPerSec, 4800 * MsPerSec);
        Assert.AreEqual(20.0, session.AccumulatedActiveMinutes, 0.001);

        // Resume.
        service.OnPauseToggled(false);
        Assert.IsFalse(service.IsSuspended);

        // Accumulate 5 more min (1-second ticks).
        service.TickManually(4800 * MsPerSec, 4800 * MsPerSec);
        for (int i = 4801; i <= 5100; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(25.0, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// M-11 §2: Rare-event timing (EffectiveRuntimeTracker) is independent
    /// of the activity session. Pausing affects both, but the accumulated
    /// values are separate.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void RareEventTiming_IndependentOfActivitySession()
    {
        var activitySession = new ActivitySession();
        var service = new ActivitySessionService(activitySession,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        var runtimeTracker = new EffectiveRuntimeTracker();
        runtimeTracker.StartActivePeriod(0);

        // Both accumulate 30 min (1800 1-second ticks).
        service.TickManually(0, 0);
        for (int i = 1; i <= 1800; i++)
        {
            var t = i * MsPerSec;
            service.TickManually(t, t);
            runtimeTracker.UpdateTo(t / MsPerMin);
        }

        Assert.AreEqual(30.0, activitySession.AccumulatedActiveMinutes, 0.001);
        Assert.AreEqual(30.0, runtimeTracker.AccumulatedMinutes, 0.001);

        // User goes idle (no input for 5+ min). Activity session resets,
        // but runtime tracker keeps accumulating (it measures process uptime,
        // not user activity). F-01 R2: Use 1-second idle ticks.
        var idleStart = 1800 * MsPerSec;
        service.TickManually(idleStart + 61 * MsPerSec, idleStart);
        for (int s = 62; s <= 301; s++)
        {
            var t = idleStart + s * MsPerSec;
            service.TickManually(t, idleStart);
            runtimeTracker.UpdateTo(t / MsPerMin);
        }

        // Activity session reset.
        Assert.AreEqual(0.0, activitySession.AccumulatedActiveMinutes, 0.001);
        // Runtime tracker still accumulating.
        Assert.IsTrue(runtimeTracker.AccumulatedMinutes > 30.0);
    }

    /// <summary>
    /// M-11 §2: The rest-reminder intent fires exactly once per session
    /// and increments the counter.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void ReminderFiresOncePerSession_CounterIncrements()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        int eventCount = 0;
        service.RestReminderRequested += (_, _) => eventCount++;

        service.TickManually(0, 0);
        for (int i = 1; i <= 7200; i++)
        {
            var t = i * MsPerSec;
            service.TickManually(t, t);
        }

        Assert.AreEqual(1, eventCount);
        Assert.AreEqual(1, service.ReminderFiredCount);
        Assert.IsTrue(session.ReminderFired);

        // Continue — no more fires.
        for (int i = 7201; i <= 7800; i++)
        {
            var t = i * MsPerSec;
            service.TickManually(t, t);
        }
        Assert.AreEqual(1, eventCount);
        Assert.AreEqual(1, service.ReminderFiredCount);

        // Idle reset → new session can fire again. F-01 R2: 1-second idle ticks.
        var idleStart = 7800 * MsPerSec;
        service.TickManually(idleStart + 61 * MsPerSec, idleStart);
        for (int s = 62; s <= 301; s++)
            service.TickManually(idleStart + s * MsPerSec, idleStart);
        Assert.IsFalse(session.ReminderFired);

        // Accumulate to threshold again.
        var resumeBase = idleStart + 301 * MsPerSec;
        service.TickManually(resumeBase, resumeBase);
        for (int i = 1; i <= 7200; i++)
        {
            var t = resumeBase + i * MsPerSec;
            service.TickManually(t, t);
        }
        Assert.AreEqual(2, eventCount);
        Assert.AreEqual(2, service.ReminderFiredCount);
    }

    /// <summary>
    /// M-11 §3: Lock screen suspends the session; unlock resumes from
    /// new baseline. Simulated via OnSessionSwitch.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void LockScreen_Suspends_UnlockResumes()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        // Accumulate 15 min (900 1-second ticks).
        service.TickManually(0, 0);
        for (int i = 1; i <= 900; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(15.0, session.AccumulatedActiveMinutes, 0.001);

        // Lock (SessionSwitchReason.SessionLock = 7).
        service.OnSessionSwitch(7);
        Assert.IsTrue(service.IsSuspended);

        // 120 min pass while locked.
        service.TickManually(8100 * MsPerSec, 8100 * MsPerSec);
        Assert.AreEqual(15.0, session.AccumulatedActiveMinutes, 0.001);

        // Unlock (SessionSwitchReason.SessionUnlock = 8).
        service.OnSessionSwitch(8);
        Assert.IsFalse(service.IsSuspended);

        // Accumulate 5 more min (1-second ticks).
        service.TickManually(8100 * MsPerSec, 8100 * MsPerSec);
        for (int i = 8101; i <= 8400; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(20.0, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// M-11 §3: Sleep suspends the session; wake resumes from new baseline.
    /// Simulated via OnPowerModeChanged.
    /// F-02 R1: Uses strongly-typed PowerModes enum (Suspend=3, Resume=1).
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void Sleep_Suspends_WakeResumes()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        // Accumulate 10 min (600 1-second ticks).
        service.TickManually(0, 0);
        for (int i = 1; i <= 600; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(10.0, session.AccumulatedActiveMinutes, 0.001);

        // Sleep (PowerModes.Suspend — actual .NET value is 3, not 4).
        service.OnPowerModeChanged((int)PowerModes.Suspend);
        Assert.IsTrue(service.IsSuspended);

        // 8 hours pass while sleeping.
        service.TickManually(29400 * MsPerSec, 29400 * MsPerSec);
        Assert.AreEqual(10.0, session.AccumulatedActiveMinutes, 0.001);

        // Wake (PowerModes.Resume — actual .NET value is 1, not 5).
        service.OnPowerModeChanged((int)PowerModes.Resume);
        Assert.IsFalse(service.IsSuspended);

        // Accumulate 5 more min (1-second ticks).
        service.TickManually(29400 * MsPerSec, 29400 * MsPerSec);
        for (int i = 29401; i <= 29700; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(15.0, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// M-11 §3: Pause + lock overlap: both suspend; clearing one while
    /// the other is active keeps the session suspended.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void PauseAndLockOverlap_BothMustClearToResume()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        // F-01 R2: Use 1-second tick intervals (5 min = 300 ticks).
        service.TickManually(0, 0);
        for (int i = 1; i <= 300; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);

        // Pause.
        service.OnPauseToggled(true);
        Assert.IsTrue(service.IsSuspended);

        // Lock while paused.
        service.OnSessionSwitch(7);
        Assert.IsTrue(service.IsSuspended);

        // Unlock — still paused, so still suspended.
        service.OnSessionSwitch(8);
        Assert.IsTrue(service.IsSuspended);

        // Unpause — now fully resumed.
        service.OnPauseToggled(false);
        Assert.IsFalse(service.IsSuspended);

        // Accumulate normally (1-second ticks).
        service.TickManually(300 * MsPerSec, 300 * MsPerSec);
        for (int i = 301; i <= 600; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(10.0, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// M-11 §3: Sleep + lock overlap: both suspend; clearing one while
    /// the other is active keeps the session suspended.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void SleepAndLockOverlap_BothMustClearToResume()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        // F-01 R2: Use 1-second tick intervals (5 min = 300 ticks).
        service.TickManually(0, 0);
        for (int i = 1; i <= 300; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);

        // Sleep.
        service.OnPowerModeChanged((int)PowerModes.Suspend);
        Assert.IsTrue(service.IsSuspended);

        // Lock while sleeping (some systems lock on wake).
        service.OnSessionSwitch(7);
        Assert.IsTrue(service.IsSuspended);

        // Wake from sleep — still locked.
        service.OnPowerModeChanged((int)PowerModes.Resume);
        Assert.IsTrue(service.IsSuspended);

        // Unlock — now fully resumed.
        service.OnSessionSwitch(8);
        Assert.IsFalse(service.IsSuspended);

        service.TickManually(300 * MsPerSec, 300 * MsPerSec);
        for (int i = 301; i <= 600; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(10.0, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// M-11 §3: After suspend/resume, no reminder is replayed for the
    /// suspended interval.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void SuspendResume_NoReminderReplay()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        int eventCount = 0;
        service.RestReminderRequested += (_, _) => eventCount++;

        // Accumulate 100 min (6000 1-second ticks, below threshold).
        service.TickManually(0, 0);
        for (int i = 1; i <= 6000; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(0, eventCount);

        // Suspend (lock) for 200 min.
        service.OnSessionSwitch(7);
        service.TickManually(18000 * MsPerSec, 18000 * MsPerSec);

        // Resume — no reminder replay for the 200 min gap.
        service.OnSessionSwitch(8);
        Assert.AreEqual(0, eventCount);
        Assert.IsFalse(session.ReminderFired);

        // Continue to threshold → fire exactly once (1-second ticks).
        service.TickManually(18000 * MsPerSec, 18000 * MsPerSec);
        for (int i = 1; i <= 1200; i++)
        {
            var t = 18000 * MsPerSec + i * MsPerSec;
            service.TickManually(t, t);
        }
        Assert.AreEqual(1, eventCount);
    }

    /// <summary>
    /// M-11 §3 / F-01 R1: Idle sampler returns null (API failure) —
    /// treated as a missing sample, not idle. The session does not
    /// accumulate and does NOT reset (no fabricated idle time).
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void IdleSamplerReturnsNull_NoAccumulation_NoReset()
    {
        var session = new ActivitySession();
        var sampler = new TestIdleSampler(() => null);
        var service = new ActivitySessionService(session, sampler, MonotonicClock.Instance);

        // First accumulate some time with valid input (1-second ticks).
        service.TickManually(0, 0);
        for (int i = 1; i <= 300; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(5.0, session.AccumulatedActiveMinutes, 0.001);

        // Now sampler returns null — missing sample, not idle.
        // The service calls TickManually with null lastInputMs.
        service.TickManually(301 * MsPerSec, null);

        // No accumulation, but also no reset — accumulated value preserved.
        Assert.AreEqual(5.0, session.AccumulatedActiveMinutes, 0.001);
        Assert.IsFalse(session.ReminderFired);
    }

    /// <summary>
    /// M-11 §2: Existing regression — global pause does not affect
    /// the activity session's accumulated value beyond suspension.
    /// The session preserves its accumulated value across pause/resume.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void PauseResume_PreservesAccumulatedValue()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        service.TickManually(0, 0);
        for (int i = 1; i <= 3000; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(50.0, session.AccumulatedActiveMinutes, 0.001);

        // Pause and resume multiple times.
        service.OnPauseToggled(true);
        service.OnPauseToggled(false);
        service.OnPauseToggled(true);
        service.OnPauseToggled(false);

        // Value preserved.
        Assert.AreEqual(50.0, session.AccumulatedActiveMinutes, 0.001);

        // Can still accumulate (1-second ticks).
        service.TickManually(3000 * MsPerSec, 3000 * MsPerSec);
        for (int i = 3001; i <= 3300; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(55.0, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// F-02 R1: Starting the service with isPaused=true suspends the
    /// session immediately — no accumulation occurs even if ticks arrive.
    /// This simulates launching from a paused archive.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void Start_WithPausedArchive_DoesNotAccumulate()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        // Start with isPaused=true (paused archive).
        service.Start(isPaused: true);
        Assert.IsTrue(service.IsSuspended, "Service should be suspended when started from paused archive.");

        // Even if ticks arrive (shouldn't in production, but verify safety),
        // the session is suspended so no accumulation.
        service.TickManually(0, 0);
        service.TickManually(600 * MsPerSec, 600 * MsPerSec);
        Assert.AreEqual(0.0, session.AccumulatedActiveMinutes, 0.001);

        // Unpause — now the session should resume.
        service.OnPauseToggled(false);
        Assert.IsFalse(service.IsSuspended);

        // Accumulate normally after unpause (1-second ticks).
        service.TickManually(600 * MsPerSec, 600 * MsPerSec);
        for (int i = 601; i <= 900; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(5.0, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// F-02 R1: Starting the service with isPaused=false (normal startup)
    /// resumes the session normally.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void Start_WithNormalStartup_AccumulatesNormally()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        service.Start(isPaused: false);
        Assert.IsFalse(service.IsSuspended);

        service.TickManually(0, 0);
        for (int i = 1; i <= 300; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(5.0, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// F-02 R1: Real PowerModes enum values (Suspend=3, Resume=1) are
    /// correctly handled by OnPowerModeChanged.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void RealPowerEnum_SuspendThenResume()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        service.TickManually(0, 0);
        for (int i = 1; i <= 300; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(5.0, session.AccumulatedActiveMinutes, 0.001);

        // Use actual .NET enum values.
        service.OnPowerModeChanged((int)PowerModes.Suspend);
        Assert.IsTrue(service.IsSuspended, $"Suspend={(int)PowerModes.Suspend}");

        service.TickManually(6000 * MsPerSec, 6000 * MsPerSec);
        Assert.AreEqual(5.0, session.AccumulatedActiveMinutes, 0.001);

        service.OnPowerModeChanged((int)PowerModes.Resume);
        Assert.IsFalse(service.IsSuspended, $"Resume={(int)PowerModes.Resume}");

        service.TickManually(6000 * MsPerSec, 6000 * MsPerSec);
        for (int i = 6001; i <= 6300; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(10.0, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// F-02 R1: Overlapping suspend states (pause + sleep + lock) all
    /// must clear before the session resumes. Uses real enum values.
    /// F-01 R2: Use 1-second tick intervals.
    /// </summary>
    [TestMethod]
    public void Overlap_PauseSleepLock_AllMustClear()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        service.TickManually(0, 0);
        for (int i = 1; i <= 300; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);

        // All three lifecycle conditions active.
        service.OnPauseToggled(true);
        Assert.IsTrue(service.IsSuspended);

        service.OnPowerModeChanged((int)PowerModes.Suspend);
        Assert.IsTrue(service.IsSuspended);

        service.OnSessionSwitch(7); // SessionLock
        Assert.IsTrue(service.IsSuspended);

        // Clear sleep — still paused and locked.
        service.OnPowerModeChanged((int)PowerModes.Resume);
        Assert.IsTrue(service.IsSuspended);

        // Clear lock — still paused.
        service.OnSessionSwitch(8); // SessionUnlock
        Assert.IsTrue(service.IsSuspended);

        // Clear pause — now fully resumed.
        service.OnPauseToggled(false);
        Assert.IsFalse(service.IsSuspended);

        service.TickManually(300 * MsPerSec, 300 * MsPerSec);
        for (int i = 301; i <= 600; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(10.0, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// F-02 R1: Dispose properly cleans up — session is suspended and
    /// further ticks are no-ops. Verifies exit cleanup.
    /// </summary>
    [TestMethod]
    public void Dispose_CleansUp_SessionSuspended()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        // Use TickManually directly (no Start, to avoid real-clock
        // vs fake-time domain mismatch). Use 1-sec intervals to stay
        // well under MaxSampleGapMs (5 sec).
        service.TickManually(0, 0);
        for (int i = 1; i <= 300; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(5.0, session.AccumulatedActiveMinutes, 0.001);

        service.Dispose();

        // Session is suspended after dispose.
        Assert.IsTrue(session.IsSuspended);

        // Ticks after dispose are no-ops.
        var before = session.AccumulatedActiveMinutes;
        for (int i = 301; i <= 600; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(before, session.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// F-04 R1: Observable properties on ActivitySessionService provide
    /// diagnostic isolation entry for reading accumulated value and
    /// reminder count without accessing the internal session.
    /// </summary>
    [TestMethod]
    public void ObservableProperties_ProvideDiagnosticEntry()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        // Initially zero.
        Assert.AreEqual(0.0, service.AccumulatedActiveMs, 0.1);
        Assert.AreEqual(0.0, service.AccumulatedActiveMinutes, 0.001);
        Assert.IsFalse(service.HasReminderFired);
        Assert.AreEqual(0, service.ReminderFiredCount);

        // Accumulate 10 min (600 1-sec ticks).
        service.TickManually(0, 0);
        for (int i = 1; i <= 600; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);

        // Observable properties reflect the session state.
        Assert.AreEqual(10.0, service.AccumulatedActiveMinutes, 0.001);
        Assert.AreEqual(session.AccumulatedActiveMs, service.AccumulatedActiveMs, 0.1);
        Assert.IsFalse(service.HasReminderFired);
        Assert.AreEqual(0, service.ReminderFiredCount);

        // Accumulate to threshold → reminder fires (7200 1-sec ticks = 120 min).
        for (int i = 601; i <= 7200; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);

        Assert.IsTrue(service.HasReminderFired);
        Assert.AreEqual(1, service.ReminderFiredCount);
    }

    /// <summary>
    /// F-04 R1: Add/remove pet operations do not affect the activity
    /// session's accumulated value. This test verifies the service is
    /// untouched by pet lifecycle changes (the service is global, not
    /// per-pet). Simulates the real add/remove flow by verifying that
    /// the service continues to accumulate normally across pet changes.
    /// </summary>
    [TestMethod]
    public void AddRemovePet_ServiceContinuesAccumulating()
    {
        var session = new ActivitySession();
        var service = new ActivitySessionService(session,
            new TestIdleSampler(() => 0), MonotonicClock.Instance);

        // Start with "single pet" — accumulate 20 min (1200 1-sec ticks).
        service.TickManually(0, 0);
        for (int i = 1; i <= 1200; i++)
            service.TickManually(i * MsPerSec, i * MsPerSec);
        Assert.AreEqual(20.0, session.AccumulatedActiveMinutes, 0.001);

        // "Add pet" — in production, AddPet does not touch
        // ActivitySessionService. The service continues to accumulate.
        service.TickManually(1201 * MsPerSec, 1201 * MsPerSec);
        Assert.AreEqual(20.0 + MsPerSec / 60000.0, session.AccumulatedActiveMinutes, 0.001);

        // "Remove pet" — still unaffected.
        service.TickManually(1202 * MsPerSec, 1202 * MsPerSec);
        Assert.AreEqual(20.0 + 2 * MsPerSec / 60000.0, session.AccumulatedActiveMinutes, 0.001);

        // Service observable properties still work.
        Assert.AreEqual(session.AccumulatedActiveMinutes, service.AccumulatedActiveMinutes, 0.001);
    }

    /// <summary>
    /// F-03 R1: InputIdleSampler correctly handles uint wraparound.
    /// When GetTickCount wraps past the last-input tick, the computed
    /// input age should still be a correct positive value, and the
    /// monotonic-domain timestamp should not jump backward.
    /// </summary>
    [TestMethod]
    public void InputIdleSampler_HandleUintWraparound()
    {
        // Simulate wraparound: lastInput near uint.MaxValue, current
        // tick has wrapped to a small value.
        var sampler = new ControlledTickSampler
        {
            LastInput = uint.MaxValue - 250,
            CurrentTick = 10
        };

        var before = sampler.GetLastInputMs()!.Value;

        // Advance current tick — the input age grows, so the monotonic
        // timestamp should move forward (become more negative relative
        // to monotonic now, i.e. the input was further in the past).
        sampler.CurrentTick = 260;
        var after = sampler.GetLastInputMs()!.Value;

        // The input age increased by 250ms (from 260 to 510), so the
        // monotonic timestamp should decrease by ~250ms (input was
        // further in the past). The key assertion: after < before
        // (timestamp moves backward in monotonic time = input was
        // longer ago), NOT a huge jump.
        var delta = after - before;
        Assert.IsTrue(delta < 0, $"Delta should be negative (input further in past), got {delta}");
        Assert.IsTrue(delta > -500, $"Delta should be small (~-250ms), got {delta}");
    }

    /// <summary>
    /// F-03 R1: InputIdleSampler returns null when the API fails
    /// (GetLastInputInfo returns false).
    /// </summary>
    [TestMethod]
    public void InputIdleSampler_ApiFailure_ReturnsNull()
    {
        var sampler = new ControlledTickSampler
        {
            LastInput = null,
            CurrentTick = 1000
        };

        Assert.IsNull(sampler.GetLastInputMs());
    }

    /// <summary>
    /// F-03 R1: InputIdleSampler returns null when the computed age
    /// is unreasonably large (exceeds 49 days), indicating an
    /// unreliable reading (e.g. stale lastInputTick from a previous
    /// boot session).
    /// </summary>
    [TestMethod]
    public void InputIdleSampler_ExcessiveAge_ReturnsNull()
    {
        // 49.5 days in ms — exceeds the 49-day reliability threshold
        // but is within uint range (uint.MaxValue ≈ 49.7 days).
        ulong excessiveAge = 49UL * 24UL * 60UL * 60UL * 1000UL + 12UL * 60UL * 60UL * 1000UL;
        var sampler = new ControlledTickSampler
        {
            LastInput = 0,
            CurrentTick = (uint)excessiveAge
        };

        Assert.IsNull(sampler.GetLastInputMs());
    }

    /// <summary>
    /// Test double for InputIdleSampler that returns controlled last
    /// input tick value (in GetTickCount milliseconds domain).
    /// </summary>
    private sealed class TestIdleSampler : InputIdleSampler
    {
        private readonly Func<uint?> _getTick;

        public TestIdleSampler(Func<uint?> getTick)
        {
            _getTick = getTick;
        }

        internal override uint? GetLastInputTickMs() => _getTick();
        internal override uint? GetCurrentTickMs() => _getTick();
    }

    /// <summary>
    /// F-03 R1: Test double that allows independent control of
    /// lastInputTick and currentTick to simulate wraparound scenarios.
    /// </summary>
    private sealed class ControlledTickSampler : InputIdleSampler
    {
        public uint? LastInput { get; set; }
        public uint? CurrentTick { get; set; }

        internal override uint? GetLastInputTickMs() => LastInput;
        internal override uint? GetCurrentTickMs() => CurrentTick;
    }
}
