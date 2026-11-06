using System.IO;
using System.Reflection;
using DesktopPet.App;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// F-06 production persistence chain tests: use real scheduler/tracker to
/// produce snapshots, save via the production mapping to isolated files,
/// reload through a fresh ArchiveService and the same production mapping,
/// then verify same-day no re-roll, remaining wait unchanged, offline not
/// counted, and consumed plans not re-firing.
/// </summary>
[TestClass]
public class RareEventPersistenceTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DesktopPet-F06-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// Full chain: run scheduler+tracker → save via production mapping →
    /// reload → restore via production mapping → verify same-day no re-roll,
    /// remaining wait preserved (4 min short → no fire, 5 min more → fire),
    /// offline not counted.
    /// </summary>
    [TestMethod]
    public void F06_ProductionChain_SaveReload_RemainingWaitPreserved()
    {
        // ── Phase 1: Run real scheduler + tracker to produce a snapshot ──
        var rng = new Random(42);
        var tracker1 = new EffectiveRuntimeTracker();
        var scheduler1 = new RareEventScheduler(0.5);

        // Start active period at monotonic minute 0.
        tracker1.StartActivePeriod(0);

        // Accumulate 100 minutes of effective runtime.
        tracker1.UpdateTo(100);

        // Create a plan for today with baseline=100.
        const string today = "2026-11-05";
        scheduler1.EnsurePlanForDate(today, rng, currentAccumulatedMinutes: tracker1.AccumulatedMinutes);

        // The plan has a delay in [20, 120]. We need to know the exact delay
        // to verify remaining wait. Read it from the plan.
        var plan1 = scheduler1.CurrentPlan!;
        var baseline = plan1.AccumulatedBaselineMinutes; // 100
        var delay = plan1.TriggerDelayMinutes;

        // Accumulate more to be 5 minutes short of the trigger.
        // dailyEffective = accumulated - baseline. We need dailyEffective = delay - 5.
        // accumulated = baseline + delay - 5.
        var targetAccumulated = baseline + delay - 5;
        tracker1.UpdateTo(targetAccumulated);

        // ── Phase 2: Save via production mapping ──
        var state = new PetStateArchive();
        App.MapRareEventObjectsToArchive(state, tracker1, scheduler1);

        var archive = new ArchiveService(_tempDir);
        archive.SaveState(state);
        Assert.IsTrue(File.Exists(archive.StateFilePath));

        // ── Phase 3: Reload through fresh ArchiveService ──
        var archive2 = new ArchiveService(_tempDir);
        var (loaded, fromBackup, unknown) = archive2.LoadState();

        Assert.IsFalse(fromBackup);
        Assert.IsFalse(unknown);
        Assert.AreEqual(PetStateArchive.CurrentSchemaVersion, loaded.SchemaVersion);

        // ── Phase 4: Restore via production mapping ──
        var (tracker2, scheduler2) = App.MapArchiveToRareEventObjects(loaded);

        // Verify accumulated minutes restored.
        Assert.AreEqual(targetAccumulated, tracker2.AccumulatedMinutes, 0.001);

        // Verify plan restored — same date, same delay, same roll, not consumed.
        Assert.IsNotNull(scheduler2.CurrentPlan);
        Assert.AreEqual(today, scheduler2.CurrentPlan!.LocalDate);
        Assert.AreEqual(delay, scheduler2.CurrentPlan.TriggerDelayMinutes);
        Assert.AreEqual(plan1.TriggerRoll, scheduler2.CurrentPlan.TriggerRoll, 0.0001);
        Assert.IsFalse(scheduler2.CurrentPlan.Consumed);
        Assert.AreEqual(baseline, scheduler2.CurrentPlan.AccumulatedBaselineMinutes, 0.001);

        // ── Phase 5: Same-day no re-roll ──
        // Calling EnsurePlanForDate with the same date should NOT create a new plan.
        bool created = scheduler2.EnsurePlanForDate(today, rng,
            currentAccumulatedMinutes: tracker2.AccumulatedMinutes);
        Assert.IsFalse(created, "Same-day EnsurePlanForDate must not re-roll.");
        Assert.AreEqual(plan1.TriggerRoll, scheduler2.CurrentPlan!.TriggerRoll, 0.0001,
            "Plan roll must be unchanged after same-day EnsurePlanForDate.");

        // ── Phase 6: Remaining wait — 4 minutes short → no fire ──
        // Start a fresh active period (simulating app restart: new monotonic base).
        tracker2.StartActivePeriod(0);
        tracker2.UpdateTo(4);
        var accumulatedAfter4 = tracker2.AccumulatedMinutes; // targetAccumulated + 4
        var dailyEffective4 = accumulatedAfter4 - baseline;

        var result4 = scheduler2.Evaluate(accumulatedAfter4, today);
        Assert.IsFalse(result4.ShouldFire,
            $"4 min short (daily={dailyEffective4}, delay={delay}) must not fire.");
        Assert.AreEqual(RareEventScheduler.DenyReason.DelayNotReached, result4.Reason);

        // ── Phase 7: 5 more minutes → delay reached → fire ──
        tracker2.UpdateTo(5);
        var accumulatedAfter5 = tracker2.AccumulatedMinutes; // targetAccumulated + 5
        var dailyEffective5 = accumulatedAfter5 - baseline;

        var result5 = scheduler2.Evaluate(accumulatedAfter5, today);
        Assert.IsTrue(dailyEffective5 >= delay,
            $"Daily effective ({dailyEffective5}) should >= delay ({delay}).");
        // Whether it fires depends on the roll. The plan should be consumed either way.
        Assert.IsTrue(result5.Reason == RareEventScheduler.DenyReason.None
                   || result5.Reason == RareEventScheduler.DenyReason.RollFailed,
            "At delay reached, result should be fire or roll-failed, not delay-not-reached.");
    }

    /// <summary>
    /// Offline time not counted: after save, a long offline gap must not
    /// inflate the accumulated minutes. The tracker's Restore only recovers
    /// the saved total; StartActivePeriod establishes a fresh monotonic base.
    /// </summary>
    [TestMethod]
    public void F06_ProductionChain_OfflineTimeNotCounted()
    {
        var rng = new Random(99);
        var tracker1 = new EffectiveRuntimeTracker();
        var scheduler1 = new RareEventScheduler(0.5);

        tracker1.StartActivePeriod(0);
        tracker1.UpdateTo(50); // 50 minutes active

        scheduler1.EnsurePlanForDate("2026-11-05", rng,
            currentAccumulatedMinutes: tracker1.AccumulatedMinutes);

        // Save.
        var state = new PetStateArchive();
        App.MapRareEventObjectsToArchive(state, tracker1, scheduler1);
        var archive = new ArchiveService(_tempDir);
        archive.SaveState(state);

        // Reload and restore.
        var (loaded, _, _) = new ArchiveService(_tempDir).LoadState();
        var (tracker2, scheduler2) = App.MapArchiveToRareEventObjects(loaded);

        // Accumulated should be exactly 50, not inflated by offline time.
        Assert.AreEqual(50.0, tracker2.AccumulatedMinutes, 0.001);

        // Start a new active period and accumulate 10 more minutes.
        tracker2.StartActivePeriod(0);
        tracker2.UpdateTo(10);

        // Total should be 60, not 60 + offline gap.
        Assert.AreEqual(60.0, tracker2.AccumulatedMinutes, 0.001);

        // Plan should still have the original baseline (50).
        Assert.IsNotNull(scheduler2.CurrentPlan);
        Assert.AreEqual(50.0, scheduler2.CurrentPlan!.AccumulatedBaselineMinutes, 0.001);

        // Daily effective = 60 - 50 = 10. If delay > 10, should not fire.
        var result = scheduler2.Evaluate(60.0, "2026-11-05");
        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DelayNotReached, result.Reason);
    }

    /// <summary>
    /// Consumed plan does not re-fire after save/reload via production mapping.
    /// </summary>
    [TestMethod]
    public void F06_ProductionChain_ConsumedPlanDoesNotRefire()
    {
        var rng = new Random(7);
        var tracker1 = new EffectiveRuntimeTracker();
        var scheduler1 = new RareEventScheduler(0.5);

        tracker1.StartActivePeriod(0);
        tracker1.UpdateTo(200); // 200 minutes active.

        scheduler1.EnsurePlanForDate("2026-11-05", rng,
            currentAccumulatedMinutes: tracker1.AccumulatedMinutes);

        // The plan's baseline is 200, so dailyEffective = 200 - 200 = 0.
        // We need to accumulate more to reach the delay. Read the delay.
        var delay = scheduler1.CurrentPlan!.TriggerDelayMinutes;
        var baseline = scheduler1.CurrentPlan.AccumulatedBaselineMinutes; // 200

        // Accumulate enough to reach the delay.
        tracker1.UpdateTo(baseline + delay + 10);

        // Evaluate to consume the plan.
        var result = scheduler1.Evaluate(tracker1.AccumulatedMinutes, "2026-11-05");
        Assert.IsTrue(result.Reason == RareEventScheduler.DenyReason.None
                   || result.Reason == RareEventScheduler.DenyReason.RollFailed,
            "Plan should be consumed (fire or roll-failed).");
        Assert.IsTrue(scheduler1.CurrentPlan!.Consumed, "Plan must be consumed after Evaluate.");

        // Save.
        var state = new PetStateArchive();
        App.MapRareEventObjectsToArchive(state, tracker1, scheduler1);
        var archive = new ArchiveService(_tempDir);
        archive.SaveState(state);

        // Reload and restore.
        var (loaded, _, _) = new ArchiveService(_tempDir).LoadState();
        var (tracker2, scheduler2) = App.MapArchiveToRareEventObjects(loaded);

        // Plan should be consumed.
        Assert.IsNotNull(scheduler2.CurrentPlan);
        Assert.IsTrue(scheduler2.CurrentPlan!.Consumed,
            "Consumed flag must survive save/reload via production mapping.");

        // Even with plenty of runtime, consumed plan must not fire.
        tracker2.StartActivePeriod(0);
        tracker2.UpdateTo(100);
        var result2 = scheduler2.Evaluate(tracker2.AccumulatedMinutes, "2026-11-05");
        Assert.IsFalse(result2.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.PlanConsumed, result2.Reason);
    }

    /// <summary>
    /// Null plan (no plan created yet) survives save/reload and allows fresh
    /// plan creation on the next day.
    /// </summary>
    [TestMethod]
    public void F06_ProductionChain_NullPlan_SurvivesReload_FreshPlanNextDay()
    {
        var tracker1 = new EffectiveRuntimeTracker();
        var scheduler1 = new RareEventScheduler(0.5);

        tracker1.StartActivePeriod(0);
        tracker1.UpdateTo(30);

        // No plan created — save with null plan.
        var state = new PetStateArchive();
        App.MapRareEventObjectsToArchive(state, tracker1, scheduler1);
        var archive = new ArchiveService(_tempDir);
        archive.SaveState(state);

        // Reload and restore.
        var (loaded, _, _) = new ArchiveService(_tempDir).LoadState();
        var (tracker2, scheduler2) = App.MapArchiveToRareEventObjects(loaded);

        Assert.IsNull(scheduler2.CurrentPlan, "Null plan must survive save/reload.");
        Assert.AreEqual(30.0, tracker2.AccumulatedMinutes, 0.001);

        // Fresh plan creation on next day should work.
        var rng = new Random(42);
        bool created = scheduler2.EnsurePlanForDate("2026-11-06", rng,
            currentAccumulatedMinutes: tracker2.AccumulatedMinutes);
        Assert.IsTrue(created);
        Assert.IsNotNull(scheduler2.CurrentPlan);
        Assert.AreEqual("2026-11-06", scheduler2.CurrentPlan!.LocalDate);
    }

    /// <summary>
    /// Invalid plan snapshot in archive is rejected by RestorePlan, preserving
    /// the scheduler's empty state. The production mapping must not crash.
    /// </summary>
    [TestMethod]
    public void F06_ProductionChain_InvalidPlanSnapshot_RejectedGracefully()
    {
        var state = new PetStateArchive
        {
            AccumulatedRuntimeMinutes = 50.0,
            RareEventPlan = new RareEventPlanSnapshot
            {
                LocalDate = "bad-date",      // invalid date
                TriggerDelayMinutes = 20,
                TriggerRoll = 0.3,
                Consumed = false,
                AccumulatedBaselineMinutes = 0
            }
        };

        var archive = new ArchiveService(_tempDir);
        archive.SaveState(state);

        var (loaded, _, _) = new ArchiveService(_tempDir).LoadState();
        var (tracker, scheduler) = App.MapArchiveToRareEventObjects(loaded);

        // Invalid plan should be rejected — scheduler has no plan.
        Assert.IsNull(scheduler.CurrentPlan,
            "Invalid plan snapshot must be rejected by production mapping.");
        Assert.AreEqual(50.0, tracker.AccumulatedMinutes, 0.001);
    }

    /// <summary>
    /// Baseline greater than accumulated (inconsistent data): the tracker
    /// restores the accumulated value, and Evaluate uses the difference
    /// (which would be negative → delay not reached). This verifies the
    /// production mapping handles inconsistent data without crashing.
    /// </summary>
    [TestMethod]
    public void F06_ProductionChain_BaselineExceedsAccumulated_NoCrash()
    {
        var state = new PetStateArchive
        {
            AccumulatedRuntimeMinutes = 30.0,
            RareEventPlan = new RareEventPlanSnapshot
            {
                LocalDate = "2026-11-05",
                TriggerDelayMinutes = 20,
                TriggerRoll = 0.1,
                Consumed = false,
                AccumulatedBaselineMinutes = 100.0 // > accumulated (30)
            }
        };

        var archive = new ArchiveService(_tempDir);
        archive.SaveState(state);

        var (loaded, _, _) = new ArchiveService(_tempDir).LoadState();
        var (tracker, scheduler) = App.MapArchiveToRareEventObjects(loaded);

        // Plan is valid (date, delay, roll, baseline all valid individually).
        Assert.IsNotNull(scheduler.CurrentPlan);

        // Evaluate: dailyEffective = 30 - 100 = -70 < delay(20) → DelayNotReached.
        var result = scheduler.Evaluate(30.0, "2026-11-05");
        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DelayNotReached, result.Reason);
    }

    // ── P1-004 lifecycle integration tests ──
    // These tests verify the App lifecycle wiring: restore → start active →
    // tick (update + ensure + evaluate) → pause/resume → save → reload.

    /// <summary>
    /// Restore from archive, start active period, advance time, ensure plan,
    /// evaluate — the full startup-to-tick chain that App.OnStartup +
    /// MainWindow.OnUpdateTick performs.
    /// </summary>
    [TestMethod]
    public void Lifecycle_RestoreStartTick_PlanCreatedAndEvaluates()
    {
        // Start with empty archive (no prior plan).
        var state = new PetStateArchive();
        var archive = new ArchiveService(_tempDir);
        archive.SaveState(state);

        // Reload and restore (same path as App.OnStartup).
        var (loaded, _, _) = new ArchiveService(_tempDir).LoadState();
        var (tracker, scheduler) = App.MapArchiveToRareEventObjects(loaded);

        // App.OnStartup: start active period at monotonic minute 0.
        tracker.StartActivePeriod(0);

        // OnUpdateTick: advance time to 50 minutes.
        tracker.UpdateTo(50);
        Assert.AreEqual(50.0, tracker.AccumulatedMinutes, 0.001);

        // EnsurePlanForDate: creates a plan with baseline=50.
        const string today = "2026-11-05";
        var created = scheduler.EnsurePlanForDate(today, new Random(42),
            currentAccumulatedMinutes: tracker.AccumulatedMinutes);
        Assert.IsTrue(created);
        Assert.IsNotNull(scheduler.CurrentPlan);
        Assert.AreEqual(50.0, scheduler.CurrentPlan!.AccumulatedBaselineMinutes, 0.001);

        // Evaluate: dailyEffective = 50 - 50 = 0 < delay → DelayNotReached.
        var result = scheduler.Evaluate(tracker.AccumulatedMinutes, today);
        Assert.IsFalse(result.ShouldFire);
    }

    /// <summary>
    /// Pause stops accumulation; resume starts a fresh active period;
    /// paused time is not counted as effective runtime.
    /// </summary>
    [TestMethod]
    public void Lifecycle_PauseResume_PausedTimeNotCounted()
    {
        var tracker = new EffectiveRuntimeTracker();
        var scheduler = new RareEventScheduler(0.5);

        // Start active at 0, accumulate 30 minutes.
        tracker.StartActivePeriod(0);
        tracker.UpdateTo(30);
        Assert.AreEqual(30.0, tracker.AccumulatedMinutes, 0.001);

        // Pause at minute 30 (App.OnPauseToggled → EndActivePeriod).
        tracker.EndActivePeriod(30);

        // Simulate 60 minutes of paused time (no UpdateTo calls during pause).
        // Resume at minute 90 (App.OnPauseToggled → StartActivePeriod).
        tracker.StartActivePeriod(90);

        // Accumulate 10 more minutes of active time.
        tracker.UpdateTo(100);

        // Total effective: 30 (before pause) + 10 (after resume) = 40.
        // The 60-minute pause gap (30→90) is NOT counted.
        Assert.AreEqual(40.0, tracker.AccumulatedMinutes, 0.001);
    }

    /// <summary>
    /// Full lifecycle: startup → tick → save → reload → restore → verify
    /// accumulated minutes and plan are preserved across the save/reload
    /// boundary, matching what App.OnStartup + MarkStateDirty + FlushFinalState
    /// performs.
    /// </summary>
    [TestMethod]
    public void Lifecycle_FullChain_SaveReload_AccumulatedAndPlanPreserved()
    {
        const string today = "2026-11-05";

        // ── Phase 1: Startup and run ──
        var tracker1 = new EffectiveRuntimeTracker();
        var scheduler1 = new RareEventScheduler(0.5);
        tracker1.StartActivePeriod(0);
        tracker1.UpdateTo(80);
        scheduler1.EnsurePlanForDate(today, new Random(42),
            currentAccumulatedMinutes: tracker1.AccumulatedMinutes);
        var plan1 = scheduler1.CurrentPlan!;
        var delay = plan1.TriggerDelayMinutes;
        var baseline = plan1.AccumulatedBaselineMinutes;

        // Advance to exactly the trigger point.
        var triggerAccumulated = baseline + delay;
        tracker1.UpdateTo(triggerAccumulated);

        // ── Phase 2: Save via production mapping (MarkStateDirty path) ──
        var state = new PetStateArchive();
        App.MapRareEventObjectsToArchive(state, tracker1, scheduler1);
        Assert.AreEqual(triggerAccumulated, state.AccumulatedRuntimeMinutes, 0.001);
        Assert.IsNotNull(state.RareEventPlan);

        var archive = new ArchiveService(_tempDir);
        archive.SaveState(state);

        // ── Phase 3: Reload and restore (App.OnStartup path) ──
        var (loaded, _, _) = new ArchiveService(_tempDir).LoadState();
        var (tracker2, scheduler2) = App.MapArchiveToRareEventObjects(loaded);

        // Accumulated minutes preserved.
        Assert.AreEqual(triggerAccumulated, tracker2.AccumulatedMinutes, 0.001);

        // Plan preserved.
        Assert.IsNotNull(scheduler2.CurrentPlan);
        Assert.AreEqual(today, scheduler2.CurrentPlan!.LocalDate);
        Assert.AreEqual(delay, scheduler2.CurrentPlan.TriggerDelayMinutes);
        Assert.AreEqual(baseline, scheduler2.CurrentPlan.AccumulatedBaselineMinutes, 0.001);

        // Same-day EnsurePlanForDate returns false (no re-roll).
        var reCreated = scheduler2.EnsurePlanForDate(today, new Random(99),
            currentAccumulatedMinutes: tracker2.AccumulatedMinutes);
        Assert.IsFalse(reCreated);

        // Start active and evaluate — should be at or past the trigger point.
        tracker2.StartActivePeriod(0);
        tracker2.UpdateTo(1); // +1 minute past restore
        var result = scheduler2.Evaluate(tracker2.AccumulatedMinutes, today);
        // dailyEffective = (triggerAccumulated + 1) - baseline = delay + 1 > delay → can fire.
        Assert.IsTrue(result.ShouldFire || result.Reason != RareEventScheduler.DenyReason.DelayNotReached,
            "After reload, remaining wait should be at or past the trigger point.");
    }

    /// <summary>
    /// Pause during active period, save while paused, reload — the paused
    /// state is preserved and the tracker's accumulated value does not
    /// increase from the offline gap.
    /// </summary>
    [TestMethod]
    public void Lifecycle_PausedExit_Reload_OfflineNotCounted()
    {
        var tracker1 = new EffectiveRuntimeTracker();
        tracker1.StartActivePeriod(0);
        tracker1.UpdateTo(40);
        tracker1.EndActivePeriod(40); // Pause at minute 40

        // Save while paused (MarkStateDirty + FlushFinalState path).
        var state = new PetStateArchive { IsPaused = true };
        App.MapRareEventObjectsToArchive(state, tracker1, new RareEventScheduler());
        var archive = new ArchiveService(_tempDir);
        archive.SaveState(state);

        // Reload (App.OnStartup path).
        var (loaded, _, _) = new ArchiveService(_tempDir).LoadState();
        var (tracker2, _) = App.MapArchiveToRareEventObjects(loaded);

        // Accumulated should be 40, not more.
        Assert.AreEqual(40.0, tracker2.AccumulatedMinutes, 0.001);

        // App.OnStartup sees IsPaused=true → does NOT call StartActivePeriod.
        // Resume later: start at minute 1000 (simulating time has passed).
        tracker2.StartActivePeriod(1000);
        tracker2.UpdateTo(1010);

        // Only 10 minutes of new active time added.
        Assert.AreEqual(50.0, tracker2.AccumulatedMinutes, 0.001);
    }

    /// <summary>
    /// Consumed plan persists across save/reload: after a plan is consumed
    /// (fired), saving and reloading shows the plan as consumed and Evaluate
    /// returns PlanConsumed, not a re-fire.
    /// </summary>
    [TestMethod]
    public void Lifecycle_ConsumedPlan_PersistsAcrossReload()
    {
        const string today = "2026-11-05";
        var tracker1 = new EffectiveRuntimeTracker();
        var scheduler1 = new RareEventScheduler(1.0); // 100% roll → always fires
        tracker1.StartActivePeriod(0);
        tracker1.UpdateTo(50);
        scheduler1.EnsurePlanForDate(today, new Random(42),
            currentAccumulatedMinutes: tracker1.AccumulatedMinutes);
        var plan = scheduler1.CurrentPlan!;
        var triggerPoint = plan.AccumulatedBaselineMinutes + plan.TriggerDelayMinutes;
        tracker1.UpdateTo(triggerPoint);

        // Evaluate and consume.
        var result = scheduler1.Evaluate(tracker1.AccumulatedMinutes, today);
        Assert.IsTrue(result.ShouldFire);
        scheduler1.MarkConsumed();
        Assert.IsTrue(scheduler1.CurrentPlan!.Consumed);

        // Save → reload → restore.
        var state = new PetStateArchive();
        App.MapRareEventObjectsToArchive(state, tracker1, scheduler1);
        var archive = new ArchiveService(_tempDir);
        archive.SaveState(state);

        var (loaded, _, _) = new ArchiveService(_tempDir).LoadState();
        var (_, scheduler2) = App.MapArchiveToRareEventObjects(loaded);

        // Plan is consumed after reload.
        Assert.IsTrue(scheduler2.CurrentPlan!.Consumed);

        // Evaluate returns PlanConsumed, not ShouldFire.
        var result2 = scheduler2.Evaluate(loaded.AccumulatedRuntimeMinutes, today);
        Assert.IsFalse(result2.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.PlanConsumed, result2.Reason);
    }

    // ── OnUpdateTick integration tests via MainWindow injection points ──
    // These tests verify that MainWindow.OnUpdateTick actually calls
    // tracker.UpdateTo, scheduler.EnsurePlanForDate, scheduler.Evaluate,
    // and scheduler.MarkConsumed — and that PauseAnimation/ResumeAnimation
    // correctly manage the active period for rare events.

    private static MainWindow CreateMainWindowOrFail()
    {
        try { return new MainWindow(); }
        catch (Exception ex)
        {
            Assert.Fail($"MainWindow construction failed: {ex.Message}");
            throw;
        }
    }

    private static void InvokeTick(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("OnUpdateTick",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method, "OnUpdateTick method not found.");
        method.Invoke(window, [null!, EventArgs.Empty]);
    }

    /// <summary>
    /// OnUpdateTick advances the runtime tracker and creates a daily plan
    /// via EnsurePlanForDate when none exists. Verifies the full tick path:
    /// UpdateTo → EnsurePlanForDate → Evaluate.
    /// </summary>
    [STATestMethod]
    public void Tick_AdvancesTracker_AndCreatesPlan()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (100, 100);

            var tracker = new EffectiveRuntimeTracker();
            var scheduler = new RareEventScheduler(0.5);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Start active period at monotonic minute 0.
            tracker.StartActivePeriod(0);

            // Tick with 50 minutes of monotonic time elapsed.
            clockMs = 50 * 60_000.0;
            InvokeTick(window);

            // Tracker should have accumulated 50 minutes.
            Assert.AreEqual(50.0, tracker.AccumulatedMinutes, 0.001,
                "Tick must advance tracker via UpdateTo.");

            // Plan should be created for today.
            Assert.IsNotNull(scheduler.CurrentPlan,
                "Tick must call EnsurePlanForDate and create a plan.");
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            Assert.AreEqual(today, scheduler.CurrentPlan!.LocalDate);
            Assert.AreEqual(50.0, scheduler.CurrentPlan.AccumulatedBaselineMinutes, 0.001);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// PauseAnimation ends the active period; ticks during pause do not
    /// advance the tracker. ResumeAnimation starts a fresh active period
    /// and subsequent ticks resume accumulation.
    /// </summary>
    [STATestMethod]
    public void Tick_PauseStopsAccumulation_ResumeRestarts()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (100, 100);

            var tracker = new EffectiveRuntimeTracker();
            var scheduler = new RareEventScheduler(0.5);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Active from minute 0, accumulate 30 minutes.
            tracker.StartActivePeriod(0);
            clockMs = 30 * 60_000.0;
            InvokeTick(window);
            Assert.AreEqual(30.0, tracker.AccumulatedMinutes, 0.001);

            // Pause at minute 30.
            window.PauseAnimation();

            // Advance clock by 60 minutes during pause and tick.
            // The tick should NOT advance the tracker because _isAppPaused
            // gates the rare-event block in OnUpdateTick.
            clockMs = 90 * 60_000.0;
            InvokeTick(window);

            // Accumulated must still be 30 — paused time not counted.
            Assert.AreEqual(30.0, tracker.AccumulatedMinutes, 0.001,
                "Paused ticks must not advance the tracker.");

            // Resume at minute 90.
            clockMs = 90 * 60_000.0;
            window.ResumeAnimation();

            // Accumulate 10 more minutes.
            clockMs = 100 * 60_000.0;
            InvokeTick(window);

            // Total: 30 (before pause) + 10 (after resume) = 40.
            Assert.AreEqual(40.0, tracker.AccumulatedMinutes, 0.001,
                "Resume must start fresh active period; only post-resume time counts.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// When Evaluate returns ShouldFire, OnUpdateTick calls MarkConsumed and
    /// MarkStateDirty. The consumed plan is persisted via the production
    /// mapping and survives save/reload.
    /// </summary>
    [STATestMethod]
    public void Tick_ShouldFire_MarksConsumed_AndPersists()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (100, 100);

            var tracker = new EffectiveRuntimeTracker();
            // 100% roll probability → always fires when delay reached.
            var scheduler = new RareEventScheduler(1.0);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Start active, create plan with baseline=0.
            tracker.StartActivePeriod(0);
            clockMs = 0;
            InvokeTick(window); // Creates plan with baseline=0.

            Assert.IsNotNull(scheduler.CurrentPlan);
            var delay = scheduler.CurrentPlan!.TriggerDelayMinutes;
            var baseline = scheduler.CurrentPlan.AccumulatedBaselineMinutes;

            // Advance to exactly the trigger point.
            var triggerMs = (baseline + delay) * 60_000.0;
            clockMs = triggerMs;
            InvokeTick(window);

            // The plan should be consumed after the tick.
            Assert.IsTrue(scheduler.CurrentPlan!.Consumed,
                "Tick with ShouldFire must call MarkConsumed.");

            // Save via production mapping and reload.
            var state = new PetStateArchive();
            App.MapRareEventObjectsToArchive(state, tracker, scheduler);
            var archive = new ArchiveService(_tempDir);
            archive.SaveState(state);

            var (loaded, _, _) = new ArchiveService(_tempDir).LoadState();
            var (_, scheduler2) = App.MapArchiveToRareEventObjects(loaded);

            // Consumed flag survives save/reload.
            Assert.IsTrue(scheduler2.CurrentPlan!.Consumed,
                "Consumed flag must persist via production mapping.");

            // Re-evaluate returns PlanConsumed, not re-fire.
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var result = scheduler2.Evaluate(loaded.AccumulatedRuntimeMinutes, today);
            Assert.IsFalse(result.ShouldFire);
            Assert.AreEqual(RareEventScheduler.DenyReason.PlanConsumed, result.Reason);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Dragging during tick does not prevent plan creation or tracker
    /// advancement, but Evaluate receives isDragging=true (which may affect
    /// denial per PRODUCT R-03 "不合适时延迟或跳过"). The tick must not
    /// crash and the tracker must still advance.
    /// </summary>
    [STATestMethod]
    public void Tick_WhileDragging_TrackerAdvances_EvaluateDenies()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (100, 100);

            var tracker = new EffectiveRuntimeTracker();
            var scheduler = new RareEventScheduler(1.0); // 100% roll
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            tracker.StartActivePeriod(0);
            clockMs = 0;
            InvokeTick(window); // Create plan.

            Assert.IsNotNull(scheduler.CurrentPlan);
            var delay = scheduler.CurrentPlan!.TriggerDelayMinutes;
            var baseline = scheduler.CurrentPlan.AccumulatedBaselineMinutes;

            // Simulate dragging by setting the _isDragging field.
            var dragField = typeof(MainWindow).GetField("_isDragging",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(dragField, "_isDragging field not found.");
            dragField!.SetValue(window, true);

            // Advance to trigger point while dragging.
            clockMs = (baseline + delay + 10) * 60_000.0;
            InvokeTick(window);

            // Tracker still advances (dragging doesn't stop time).
            Assert.AreEqual(baseline + delay + 10, tracker.AccumulatedMinutes, 0.001,
                "Tracker must advance even while dragging.");

            // Plan should NOT be consumed because isDragging=true denies fire.
            Assert.IsFalse(scheduler.CurrentPlan!.Consumed,
                "Plan must not be consumed while dragging (Evaluate denies fire).");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Full production chain: tick creates plan → tick triggers fire →
    /// save via production mapping → reload → restore → new day →
    /// EnsurePlanForDate creates fresh plan with correct baseline.
    /// This verifies the entire App lifecycle: OnStartup → OnUpdateTick →
    /// MarkStateDirty → FlushFinalState → OnStartup(reload).
    /// </summary>
    [STATestMethod]
    public void Tick_FullLifecycle_Fire_SaveReload_NextDayFreshPlan()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (100, 100);

            var tracker = new EffectiveRuntimeTracker();
            var scheduler = new RareEventScheduler(1.0); // 100% roll
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // ── Day 1: Startup + tick to create plan + advance to fire ──
            tracker.StartActivePeriod(0);
            clockMs = 0;
            InvokeTick(window); // baseline=0

            var plan1 = scheduler.CurrentPlan!;
            var delay1 = plan1.TriggerDelayMinutes;
            var baseline1 = plan1.AccumulatedBaselineMinutes;

            // Advance past the trigger point.
            clockMs = (baseline1 + delay1 + 5) * 60_000.0;
            InvokeTick(window);

            Assert.IsTrue(scheduler.CurrentPlan!.Consumed,
                "Day 1 plan must be consumed after trigger.");

            var totalAccumulated = tracker.AccumulatedMinutes;

            // ── Save via production mapping ──
            var state = new PetStateArchive();
            App.MapRareEventObjectsToArchive(state, tracker, scheduler);
            Assert.AreEqual(totalAccumulated, state.AccumulatedRuntimeMinutes, 0.001);
            Assert.IsNotNull(state.RareEventPlan);
            Assert.IsTrue(state.RareEventPlan!.Consumed);

            var archive = new ArchiveService(_tempDir);
            archive.SaveState(state);

            // ── Reload and restore ──
            var (loaded, _, _) = new ArchiveService(_tempDir).LoadState();
            var (tracker2, scheduler2) = App.MapArchiveToRareEventObjects(loaded);

            Assert.AreEqual(totalAccumulated, tracker2.AccumulatedMinutes, 0.001);
            Assert.IsTrue(scheduler2.CurrentPlan!.Consumed);

            // ── Day 2: Fresh plan with baseline = totalAccumulated ──
            var tomorrow = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");
            var created = scheduler2.EnsurePlanForDate(tomorrow, new Random(42),
                currentAccumulatedMinutes: tracker2.AccumulatedMinutes);
            Assert.IsTrue(created, "Day 2 must create a fresh plan.");
            Assert.AreEqual(tomorrow, scheduler2.CurrentPlan!.LocalDate);
            Assert.AreEqual(totalAccumulated, scheduler2.CurrentPlan.AccumulatedBaselineMinutes, 0.001);
            Assert.IsFalse(scheduler2.CurrentPlan.Consumed);

            // Day 2 evaluate with 0 new effective minutes → DelayNotReached.
            tracker2.StartActivePeriod(0);
            tracker2.UpdateTo(5);
            var result = scheduler2.Evaluate(tracker2.AccumulatedMinutes, tomorrow);
            // dailyEffective = totalAccumulated + 5 - totalAccumulated = 5.
            // If delay > 5, DelayNotReached. If delay <= 5, may fire.
            // Either way, it must NOT be PlanConsumed.
            Assert.AreNotEqual(RareEventScheduler.DenyReason.PlanConsumed, result.Reason,
                "Day 2 fresh plan must not be PlanConsumed.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── F-08: Save queue notification tests via real App.MarkStateDirty path ──
    // These tests verify that OnUpdateTick notifies the real App save queue
    // (MarkStateDirty → SaveCoordinator → file) when the plan state changes,
    // not only when ShouldFire is true. Tests do NOT use TestRuntimeTracker/
    // TestRareEventScheduler — they go through the real App.Current path.
    // Tests also verify that no-state-change ticks do NOT mark dirty (avoiding
    // debounce reset every tick).

    /// <summary>
    /// Helper: get or create an App instance for testing. WPF Application is
    /// a per-AppDomain singleton, so we reuse Application.Current if it's
    /// already an App, or create a new one.
    /// </summary>
    private static App GetOrCreateTestApp(string isolatedDir,
        EffectiveRuntimeTracker? tracker = null, RareEventScheduler? scheduler = null)
    {
        App app;
        if (System.Windows.Application.Current is App existing)
        {
            app = existing;
        }
        else
        {
            app = new App();
        }
        app.TestSetupIsolatedSave(isolatedDir,
            tracker: tracker, scheduler: scheduler);
        return app;
    }

    /// <summary>
    /// Helper: get the _lastRareEventPlanDate / _lastRareEventPlanConsumed
    /// fields from a MainWindow for verifying dirty-tracking state.
    /// </summary>
    private static (string? date, bool? consumed) GetLastRareEventPlanState(MainWindow window)
    {
        var dateField = typeof(MainWindow).GetField("_lastRareEventPlanDate",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var consumedField = typeof(MainWindow).GetField("_lastRareEventPlanConsumed",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(dateField);
        Assert.IsNotNull(consumedField);
        return ((string?)dateField!.GetValue(window), (bool?)consumedField!.GetValue(window));
    }

    /// <summary>
    /// F-08: Tick that creates a new plan must notify the real App save queue
    /// and result in the plan being written to an isolated file via the real
    /// SaveCoordinator. Not a test-injected Map/Save — the tick itself drives
    /// the production MarkStateDirty → SaveCoordinator → Flush path.
    /// </summary>
    [STATestMethod]
    public void F08_TickCreatesPlan_NotifiesRealSaveQueue_FileWritten()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DesktopPet-F08-create-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var tracker = new EffectiveRuntimeTracker();
            var scheduler = new RareEventScheduler(0.5);
            var app = GetOrCreateTestApp(tempDir, tracker: tracker, scheduler: scheduler);

            // Start active period (same as App.OnStartup does when not paused).
            tracker.StartActivePeriod(0);

            var window = CreateMainWindowOrFail();
            try
            {
                // Do NOT set TestRuntimeTracker/TestRareEventScheduler —
                // tick must go through App.Current path.
                var clockMs = 0.0;
                window.TestGetElapsedMs = () => clockMs;
                window.TestGetCursorPos = () => (100, 100);

                // Tick at 0 minutes — should create a plan.
                clockMs = 0;
                InvokeTick(window);

                // Plan should be created.
                Assert.IsNotNull(scheduler.CurrentPlan, "Tick must create a plan via App.Current path.");

                // The dirty-tracking fields should be updated.
                var (date, consumed) = GetLastRareEventPlanState(window);
                Assert.IsNotNull(date, "Plan date should be tracked after creation.");
                Assert.IsFalse(consumed ?? true, "New plan should not be consumed.");

                // Flush the real SaveCoordinator — file should contain the plan.
                app.FlushFinalState();

                // Verify file was written with the plan.
                var archive = new ArchiveService(tempDir);
                var (loaded, fromBackup, unknown) = archive.LoadState();
                Assert.IsFalse(fromBackup);
                Assert.IsFalse(unknown);
                Assert.IsNotNull(loaded.RareEventPlan, "Plan must be persisted via real save queue.");
                Assert.AreEqual(scheduler.CurrentPlan!.LocalDate, loaded.RareEventPlan!.LocalDate);
                Assert.IsFalse(loaded.RareEventPlan.Consumed);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// F-08: Tick where roll fails (roll >= triggerProbability) must still
    /// notify the save queue because the plan's Consumed flag changes from
    /// false to true. The consumed plan must be persisted to file.
    /// </summary>
    [STATestMethod]
    public void F08_TickRollFailed_NotifiesSaveQueue_ConsumedPersisted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DesktopPet-F08-rollfail-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var tracker = new EffectiveRuntimeTracker();
            // Use 0.0 probability → roll always fails (roll >= 0.0 is always true).
            // Actually roll is [0,1), so with probability 0.0, roll >= 0.0 → always RollFailed.
            var scheduler = new RareEventScheduler(0.0);
            var app = GetOrCreateTestApp(tempDir, tracker: tracker, scheduler: scheduler);

            tracker.StartActivePeriod(0);

            var window = CreateMainWindowOrFail();
            try
            {
                var clockMs = 0.0;
                window.TestGetElapsedMs = () => clockMs;
                window.TestGetCursorPos = () => (100, 100);

                // First tick: create plan.
                clockMs = 0;
                InvokeTick(window);
                Assert.IsNotNull(scheduler.CurrentPlan);

                var plan = scheduler.CurrentPlan!;
                var triggerPoint = plan.AccumulatedBaselineMinutes + plan.TriggerDelayMinutes;

                // Advance to trigger point.
                clockMs = triggerPoint * 60_000.0;
                InvokeTick(window);

                // Plan should be consumed (RollFailed still consumes).
                Assert.IsTrue(scheduler.CurrentPlan!.Consumed,
                    "RollFailed must consume the plan.");

                // Flush real save queue.
                app.FlushFinalState();

                // Verify consumed plan is in the file.
                var archive = new ArchiveService(tempDir);
                var (loaded, _, _) = archive.LoadState();
                Assert.IsNotNull(loaded.RareEventPlan);
                Assert.IsTrue(loaded.RareEventPlan!.Consumed,
                    "Consumed (roll-failed) plan must be persisted via real save queue.");

                // Reload into fresh scheduler — must not re-fire.
                var (_, scheduler2) = App.MapArchiveToRareEventObjects(loaded);
                Assert.IsTrue(scheduler2.CurrentPlan!.Consumed);
                var loadedPlanDate = loaded.RareEventPlan!.LocalDate ?? string.Empty;
                var result = scheduler2.Evaluate(loaded.AccumulatedRuntimeMinutes,
                    loadedPlanDate);
                Assert.IsFalse(result.ShouldFire);
                Assert.AreEqual(RareEventScheduler.DenyReason.PlanConsumed, result.Reason);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// F-08: Tick where ShouldFire is true must notify save queue and persist
    /// the consumed plan. This was already working before F-08, but we verify
    /// it still works through the real App path (not just test injection).
    /// </summary>
    [STATestMethod]
    public void F08_TickShouldFire_NotifiesSaveQueue_ConsumedPersisted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DesktopPet-F08-fire-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var tracker = new EffectiveRuntimeTracker();
            // 1.0 probability → always fires when delay reached.
            var scheduler = new RareEventScheduler(1.0);
            var app = GetOrCreateTestApp(tempDir, tracker: tracker, scheduler: scheduler);

            tracker.StartActivePeriod(0);

            var window = CreateMainWindowOrFail();
            try
            {
                var clockMs = 0.0;
                window.TestGetElapsedMs = () => clockMs;
                window.TestGetCursorPos = () => (100, 100);

                // Create plan.
                clockMs = 0;
                InvokeTick(window);
                Assert.IsNotNull(scheduler.CurrentPlan);

                var plan = scheduler.CurrentPlan!;
                var triggerPoint = plan.AccumulatedBaselineMinutes + plan.TriggerDelayMinutes;

                // Advance to trigger → ShouldFire.
                clockMs = triggerPoint * 60_000.0;
                InvokeTick(window);

                Assert.IsTrue(scheduler.CurrentPlan!.Consumed,
                    "ShouldFire must consume the plan.");

                // Flush real save queue.
                app.FlushFinalState();

                // Verify consumed plan in file.
                var archive = new ArchiveService(tempDir);
                var (loaded, _, _) = archive.LoadState();
                Assert.IsNotNull(loaded.RareEventPlan);
                Assert.IsTrue(loaded.RareEventPlan!.Consumed);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// F-08: Ticks that do NOT change plan state (same plan, delay not reached,
    /// not consumed) must NOT call MarkStateDirty. This is verified by checking
    /// that the dirty-tracking fields remain unchanged after a no-change tick.
    /// This prevents the 5-second debounce from being reset every tick.
    /// </summary>
    [STATestMethod]
    public void F08_NoStateChangeTick_DoesNotMarkDirty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DesktopPet-F08-nochange-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var tracker = new EffectiveRuntimeTracker();
            var scheduler = new RareEventScheduler(0.5);
            var app = GetOrCreateTestApp(tempDir, tracker: tracker, scheduler: scheduler);

            tracker.StartActivePeriod(0);

            var window = CreateMainWindowOrFail();
            try
            {
                var clockMs = 0.0;
                window.TestGetElapsedMs = () => clockMs;
                window.TestGetCursorPos = () => (100, 100);

                // First tick: create plan (state changes → dirty).
                clockMs = 0;
                InvokeTick(window);

                var (dateAfter1, consumedAfter1) = GetLastRareEventPlanState(window);
                Assert.IsNotNull(dateAfter1);

                // Second tick: same plan, delay not reached (no state change).
                clockMs = 1 * 60_000.0; // 1 minute later, not enough for delay.
                InvokeTick(window);

                var (dateAfter2, consumedAfter2) = GetLastRareEventPlanState(window);

                // Dirty-tracking fields must NOT change.
                Assert.AreEqual(dateAfter1, dateAfter2,
                    "Plan date must not change on no-state-change tick.");
                Assert.AreEqual(consumedAfter1, consumedAfter2,
                    "Consumed flag must not change on no-state-change tick.");

                // Third tick: still no change.
                clockMs = 2 * 60_000.0;
                InvokeTick(window);

                var (dateAfter3, consumedAfter3) = GetLastRareEventPlanState(window);
                Assert.AreEqual(dateAfter2, dateAfter3);
                Assert.AreEqual(consumedAfter2, consumedAfter3);

                // Plan should still not be consumed.
                Assert.IsFalse(scheduler.CurrentPlan!.Consumed);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// F-08: Full chain — tick creates plan → tick roll-fails → flush real
    /// save → reload from isolated file → restore via production mapping →
    /// verify consumed plan does not re-fire. This closes the production
    /// save-queue chain end-to-end through the real App path.
    /// </summary>
    [STATestMethod]
    public void F08_FullChain_Create_RollFail_Save_Restore_NoRefire()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DesktopPet-F08-chain-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var tracker = new EffectiveRuntimeTracker();
            var scheduler = new RareEventScheduler(0.0); // always roll-fails
            var app = GetOrCreateTestApp(tempDir, tracker: tracker, scheduler: scheduler);

            tracker.StartActivePeriod(0);

            var window = CreateMainWindowOrFail();
            try
            {
                var clockMs = 0.0;
                window.TestGetElapsedMs = () => clockMs;
                window.TestGetCursorPos = () => (100, 100);

                // Tick 1: create plan.
                clockMs = 0;
                InvokeTick(window);
                Assert.IsNotNull(scheduler.CurrentPlan);
                Assert.IsFalse(scheduler.CurrentPlan!.Consumed);

                var plan = scheduler.CurrentPlan!;
                var triggerPoint = plan.AccumulatedBaselineMinutes + plan.TriggerDelayMinutes;

                // Tick 2: advance to trigger → RollFailed → consumed.
                clockMs = triggerPoint * 60_000.0;
                InvokeTick(window);
                Assert.IsTrue(scheduler.CurrentPlan!.Consumed);

                // Flush real save queue (simulates app exit).
                app.FlushFinalState();

                // Verify file exists and contains consumed plan.
                Assert.IsTrue(File.Exists(new ArchiveService(tempDir).StateFilePath));

                // Reload from isolated file via fresh ArchiveService.
                var (loaded, fromBackup, unknown) = new ArchiveService(tempDir).LoadState();
                Assert.IsFalse(fromBackup);
                Assert.IsFalse(unknown);

                // Restore via production mapping.
                var (tracker2, scheduler2) = App.MapArchiveToRareEventObjects(loaded);

                // Plan must be consumed after restore.
                Assert.IsNotNull(scheduler2.CurrentPlan);
                Assert.IsTrue(scheduler2.CurrentPlan!.Consumed,
                    "Consumed plan must survive save/reload through real App save queue.");

                // Accumulated minutes preserved.
                Assert.AreEqual(tracker.AccumulatedMinutes, tracker2.AccumulatedMinutes, 0.001);

                // Evaluate must not re-fire.
                tracker2.StartActivePeriod(0);
                tracker2.UpdateTo(10);
                var result = scheduler2.Evaluate(tracker2.AccumulatedMinutes,
                    scheduler2.CurrentPlan.LocalDate);
                Assert.IsFalse(result.ShouldFire);
                Assert.AreEqual(RareEventScheduler.DenyReason.PlanConsumed, result.Reason);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
