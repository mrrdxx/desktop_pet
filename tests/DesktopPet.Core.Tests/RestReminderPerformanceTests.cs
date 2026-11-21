using DesktopPet.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Phase = DesktopPet.Core.Events.RestReminderPerformance.Phase;
using EndReason = DesktopPet.Core.Events.RestReminderPerformance.EndReason;

namespace DesktopPet.Core.Tests;

/// <summary>
/// P4-002 M-12 §1: Unit tests for <see cref="RestReminderPerformance"/>.
/// <para>
/// Verifies the Core state machine's phase transitions, natural completion,
/// skip, cancel, lifecycle cancel, and resume semantics without any App
/// or animation dependencies.
/// </para>
/// </summary>
[TestClass]
public class RestReminderPerformanceTests
{
    /// <summary>
    /// Start with a valid stretch duration enters the Stretching phase.
    /// </summary>
    [TestMethod]
    public void Start_WithStretchDuration_EntersStretching()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600);

        Assert.AreEqual(Phase.Stretching, perf.CurrentPhase);
        Assert.IsTrue(perf.IsActive);
        Assert.IsFalse(perf.IsCompleted);
        Assert.AreEqual(EndReason.None, perf.Reason);
        Assert.AreEqual(1600, perf.StretchDurationMs);
        Assert.AreEqual(30000, perf.TotalDurationMs);
    }

    /// <summary>
    /// Start with zero stretch duration skips directly to Resting.
    /// </summary>
    [TestMethod]
    public void Start_WithZeroStretch_SkipsToResting()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 0);

        Assert.AreEqual(Phase.Resting, perf.CurrentPhase);
        Assert.IsTrue(perf.IsActive);
    }

    /// <summary>
    /// Tick during Stretching phase returns "stretch".
    /// </summary>
    [TestMethod]
    public void Tick_DuringStretching_ReturnsStretch()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600);

        var action = perf.Tick(800);
        Assert.AreEqual("stretch", action);
        Assert.AreEqual(Phase.Stretching, perf.CurrentPhase);
    }

    /// <summary>
    /// Tick after stretch duration transitions to Resting and returns "rest".
    /// </summary>
    [TestMethod]
    public void Tick_AfterStretchDuration_TransitionsToResting()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600);

        // Still stretching at 1599ms.
        Assert.AreEqual("stretch", perf.Tick(1599));
        // At 1600ms, transition to resting.
        Assert.AreEqual("rest", perf.Tick(1600));
        Assert.AreEqual(Phase.Resting, perf.CurrentPhase);
    }

    /// <summary>
    /// Tick during Resting phase returns "rest".
    /// </summary>
    [TestMethod]
    public void Tick_DuringResting_ReturnsRest()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600);

        perf.Tick(1600); // Transition to resting.
        var action = perf.Tick(20000);
        Assert.AreEqual("rest", action);
        Assert.AreEqual(Phase.Resting, perf.CurrentPhase);
    }

    /// <summary>
    /// Natural completion at TotalDurationMs returns null and sets Completed.
    /// </summary>
    [TestMethod]
    public void Tick_AtTotalDuration_CompletesNaturally()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600, totalDurationMs: 30000);

        perf.Tick(1600); // → Resting
        Assert.AreEqual("rest", perf.Tick(29999));
        // At 30000ms, natural completion.
        var action = perf.Tick(30000);
        Assert.IsNull(action);
        Assert.AreEqual(Phase.Completed, perf.CurrentPhase);
        Assert.AreEqual(EndReason.NaturalCompletion, perf.Reason);
        Assert.IsTrue(perf.IsCompleted);
        Assert.IsFalse(perf.IsActive);
    }

    /// <summary>
    /// Natural completion with custom total duration.
    /// </summary>
    [TestMethod]
    public void Tick_CustomTotalDuration_CompletesAtCustomTime()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1000, totalDurationMs: 5000);

        perf.Tick(1000); // → Resting
        var action = perf.Tick(5000);
        Assert.IsNull(action);
        Assert.AreEqual(EndReason.NaturalCompletion, perf.Reason);
    }

    /// <summary>
    /// Skip immediately transitions to Completed with Skipped reason.
    /// </summary>
    [TestMethod]
    public void Skip_DuringStretching_EndsImmediately()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600);
        perf.Tick(500); // Mid-stretch.

        perf.Skip();

        Assert.AreEqual(Phase.Completed, perf.CurrentPhase);
        Assert.AreEqual(EndReason.Skipped, perf.Reason);
        Assert.IsTrue(perf.IsCompleted);
        Assert.IsFalse(perf.IsActive);
    }

    /// <summary>
    /// Skip during Resting phase also works.
    /// </summary>
    [TestMethod]
    public void Skip_DuringResting_EndsImmediately()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600);
        perf.Tick(1600); // → Resting

        perf.Skip();

        Assert.AreEqual(EndReason.Skipped, perf.Reason);
    }

    /// <summary>
    /// Cancel transitions to Completed with Cancelled reason.
    /// </summary>
    [TestMethod]
    public void Cancel_DuringActive_EndsWithCancelled()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600);
        perf.Tick(500);

        perf.Cancel();

        Assert.AreEqual(Phase.Completed, perf.CurrentPhase);
        Assert.AreEqual(EndReason.Cancelled, perf.Reason);
    }

    /// <summary>
    /// LifecycleCancel transitions to Completed with LifecycleInterrupt reason.
    /// </summary>
    [TestMethod]
    public void LifecycleCancel_DuringActive_EndsWithLifecycleInterrupt()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600);
        perf.Tick(500);

        perf.LifecycleCancel();

        Assert.AreEqual(Phase.Completed, perf.CurrentPhase);
        Assert.AreEqual(EndReason.LifecycleInterrupt, perf.Reason);
    }

    /// <summary>
    /// Skip/Cancel/LifecycleCancel on an idle state machine is a no-op.
    /// </summary>
    [TestMethod]
    public void SkipCancel_OnIdle_IsNoOp()
    {
        var perf = new RestReminderPerformance();

        perf.Skip();
        Assert.AreEqual(Phase.Idle, perf.CurrentPhase);
        Assert.AreEqual(EndReason.None, perf.Reason);

        perf.Cancel();
        Assert.AreEqual(Phase.Idle, perf.CurrentPhase);

        perf.LifecycleCancel();
        Assert.AreEqual(Phase.Idle, perf.CurrentPhase);
    }

    /// <summary>
    /// Tick on idle or completed returns null.
    /// </summary>
    [TestMethod]
    public void Tick_OnIdleOrCompleted_ReturnsNull()
    {
        var perf = new RestReminderPerformance();

        Assert.IsNull(perf.Tick(100));

        perf.Start(0, stretchDurationMs: 1000, totalDurationMs: 2000);
        perf.Tick(2000); // Natural completion.
        Assert.IsNull(perf.Tick(3000));
    }

    /// <summary>
    /// Reset returns the state machine to Idle with all fields cleared.
    /// </summary>
    [TestMethod]
    public void Reset_ClearsAllState()
    {
        var perf = new RestReminderPerformance();
        perf.Start(100, stretchDurationMs: 1600, totalDurationMs: 30000);
        perf.Tick(500);
        perf.Skip();

        perf.Reset();

        Assert.AreEqual(Phase.Idle, perf.CurrentPhase);
        Assert.AreEqual(EndReason.None, perf.Reason);
        Assert.AreEqual(0, perf.StretchDurationMs);
        Assert.AreEqual(0, perf.TotalDurationMs);
        Assert.IsFalse(perf.IsActive);
        Assert.IsFalse(perf.IsCompleted);
    }

    /// <summary>
    /// After Reset, a new Start can begin a fresh performance.
    /// </summary>
    [TestMethod]
    public void Reset_AllowsNewStart()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1000, totalDurationMs: 5000);
        perf.Tick(5000); // Complete.
        perf.Reset();

        perf.Start(100, stretchDurationMs: 2000, totalDurationMs: 10000);
        Assert.AreEqual(Phase.Stretching, perf.CurrentPhase);
        Assert.AreEqual(2000, perf.StretchDurationMs);
        Assert.AreEqual(10000, perf.TotalDurationMs);
    }

    /// <summary>
    /// Resume compensates the internal clock so the performance continues
    /// from where it was paused (pause duration is subtracted from elapsed).
    /// </summary>
    [TestMethod]
    public void Resume_CompensatesPauseDuration()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600, totalDurationMs: 10000);

        // At 2000ms, we're in Resting (stretch ended at 1600ms).
        Assert.AreEqual("rest", perf.Tick(2000));

        // Simulate a 3000ms pause. After resume, the internal clock is
        // compensated by 3000ms so the performance doesn't lose time.
        perf.Resume(3000);

        // At 5000ms wall-clock, the effective elapsed is 5000 - 3000 = 2000ms.
        // The performance should still be in Resting, not completed.
        Assert.AreEqual("rest", perf.Tick(5000));
        Assert.AreEqual(Phase.Resting, perf.CurrentPhase);

        // At 13000ms wall-clock, effective elapsed = 13000 - 3000 = 10000ms
        // → natural completion.
        Assert.IsNull(perf.Tick(13000));
        Assert.AreEqual(EndReason.NaturalCompletion, perf.Reason);
    }

    /// <summary>
    /// Resume on idle state machine is a no-op.
    /// </summary>
    [TestMethod]
    public void Resume_OnIdle_IsNoOp()
    {
        var perf = new RestReminderPerformance();
        perf.Resume(5000);
        Assert.AreEqual(Phase.Idle, perf.CurrentPhase);
    }

    /// <summary>
    /// Resume with zero or negative duration is a no-op.
    /// </summary>
    [TestMethod]
    public void Resume_WithZeroOrNegative_IsNoOp()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600);
        perf.Tick(100);

        perf.Resume(0);
        perf.Resume(-100);

        // No compensation added — tick still works normally.
        Assert.AreEqual("stretch", perf.Tick(200));
    }

    /// <summary>
    /// Full lifecycle: Start → Stretching → Resting → NaturalCompletion → Reset.
    /// </summary>
    [TestMethod]
    public void FullLifecycle_StretchRestComplete_Reset()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600, totalDurationMs: 10000);

        // Stretching phase.
        Assert.AreEqual("stretch", perf.Tick(0));
        Assert.AreEqual("stretch", perf.Tick(800));
        Assert.AreEqual(Phase.Stretching, perf.CurrentPhase);

        // Transition to Resting.
        Assert.AreEqual("rest", perf.Tick(1600));
        Assert.AreEqual(Phase.Resting, perf.CurrentPhase);

        // Resting phase.
        Assert.AreEqual("rest", perf.Tick(5000));
        Assert.AreEqual("rest", perf.Tick(9999));

        // Natural completion.
        Assert.IsNull(perf.Tick(10000));
        Assert.AreEqual(EndReason.NaturalCompletion, perf.Reason);
        Assert.IsTrue(perf.IsCompleted);

        // Reset for next use.
        perf.Reset();
        Assert.AreEqual(Phase.Idle, perf.CurrentPhase);
        Assert.IsFalse(perf.IsActive);
    }

    /// <summary>
    /// Stretch-only performance (no rest phase) when total equals stretch duration.
    /// </summary>
    [TestMethod]
    public void TotalEqualsStretch_CompletesAtStretchEnd()
    {
        var perf = new RestReminderPerformance();
        perf.Start(0, stretchDurationMs: 1600, totalDurationMs: 1600);

        // At 1599ms, still stretching.
        Assert.AreEqual("stretch", perf.Tick(1599));
        // At 1600ms, both stretch ends AND total ends → completion takes priority.
        var action = perf.Tick(1600);
        Assert.IsNull(action);
        Assert.AreEqual(EndReason.NaturalCompletion, perf.Reason);
    }

    /// <summary>
    /// DefaultTotalDurationMs is 30000 (approximately 30 seconds per R-06).
    /// </summary>
    [TestMethod]
    public void DefaultTotalDuration_Is30Seconds()
    {
        Assert.AreEqual(30000.0, RestReminderPerformance.DefaultTotalDurationMs);
    }
}
