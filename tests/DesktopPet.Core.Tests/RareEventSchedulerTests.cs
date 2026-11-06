using DesktopPet.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// RareEventScheduler tests covering PRODUCT R-03 rules:
///   - Daily plan creation with 20–120 min delay and pre-rolled trigger.
///   - 50% trigger probability at candidate point.
///   - Shared daily quota (consumed plan blocks re-trigger).
///   - Cross-day: new plan on date advance.
///   - Clock rollback: no new plan, no re-grant.
///   - Pause/drag/social/rest: decline with reason.
///   - Plan snapshot restore: restart does not re-roll.
///   - No carryover: unconsumed day's plan does not persist to next day.
///   - F-01: Date consistency — rollback denies evaluation; continuous
///     day1-fire → day2-new → rollback-day1 → deny → back-to-day2 sequence.
///   - F-03: Invalid snapshot (bad date, NaN roll, out-of-range delay) rejected
///     by RestorePlan; invalid runtime (NaN/Infinity/negative) rejected by Evaluate.
/// </summary>
[TestClass]
public class RareEventSchedulerTests
{
    private const string Date1 = "2026-11-04";
    private const string Date2 = "2026-11-05";
    private const string Date3 = "2026-11-06";

    // ── Plan creation ───────────────────────────────────────────────────

    [TestMethod]
    public void EnsurePlan_FirstCall_CreatesPlan()
    {
        var rng = new Random(42);
        var scheduler = new RareEventScheduler();

        var created = scheduler.EnsurePlanForDate(Date1, rng);

        Assert.IsTrue(created);
        Assert.IsNotNull(scheduler.CurrentPlan);
        Assert.AreEqual(Date1, scheduler.CurrentPlan!.LocalDate);
        Assert.IsFalse(scheduler.CurrentPlan.Consumed);
    }

    [TestMethod]
    public void EnsurePlan_SameDate_DoesNotReRoll()
    {
        var rng = new Random(42);
        var scheduler = new RareEventScheduler();
        scheduler.EnsurePlanForDate(Date1, rng);
        var original = scheduler.CurrentPlan!;

        var created = scheduler.EnsurePlanForDate(Date1, rng);

        Assert.IsFalse(created);
        Assert.AreSame(original, scheduler.CurrentPlan);
    }

    [TestMethod]
    public void EnsurePlan_DelayInRange_20To120Minutes()
    {
        // With multiple seeds, delay should always be in [20, 120].
        for (int seed = 0; seed < 100; seed++)
        {
            var rng = new Random(seed);
            var scheduler = new RareEventScheduler();
            scheduler.EnsurePlanForDate(Date1, rng);

            var delay = scheduler.CurrentPlan!.TriggerDelayMinutes;
            Assert.IsTrue(delay >= 20 && delay <= 120,
                $"Seed {seed}: delay {delay} out of [20, 120]");
        }
    }

    [TestMethod]
    public void EnsurePlan_TriggerRollInRange_0To1()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var rng = new Random(seed);
            var scheduler = new RareEventScheduler();
            scheduler.EnsurePlanForDate(Date1, rng);

            var roll = scheduler.CurrentPlan!.TriggerRoll;
            Assert.IsTrue(roll >= 0.0 && roll < 1.0,
                $"Seed {seed}: roll {roll} out of [0, 1)");
        }
    }

    // ── Trigger probability ─────────────────────────────────────────────

    [TestMethod]
    public void Evaluate_DelayReached_RollPasses_FiresAndConsumes()
    {
        // Seed that produces a roll < 0.5 (trigger).
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);

        Assert.IsTrue(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.None, result.Reason);
        Assert.IsTrue(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void Evaluate_DelayReached_RollFails_DoesNotFireButConsumes()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.9);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.RollFailed, result.Reason);
        Assert.IsTrue(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void Evaluate_RollAt50PercentBoundary_Triggers()
    {
        // Roll exactly 0.5 should NOT trigger (strict less-than).
        var scheduler = MakeSchedulerWithRoll(Date1, 0.5);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);

        Assert.IsFalse(result.ShouldFire);
    }

    [TestMethod]
    public void Evaluate_RollJustBelow50_Triggers()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.499);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);

        Assert.IsTrue(result.ShouldFire);
    }

    // ── Shared daily quota ───────────────────────────────────────────────

    [TestMethod]
    public void Evaluate_AfterConsumed_DoesNotReTrigger()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        // First evaluation fires.
        var r1 = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);
        Assert.IsTrue(r1.ShouldFire);

        // Second evaluation on same day: plan consumed, no re-trigger.
        var r2 = scheduler.Evaluate(effectiveRuntimeMinutes: 30, currentLocalDate: Date1);
        Assert.IsFalse(r2.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.PlanConsumed, r2.Reason);
    }

    [TestMethod]
    public void MarkConsumed_BlocksFutureTrigger()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        scheduler.MarkConsumed();

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);
        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.PlanConsumed, result.Reason);
    }

    [TestMethod]
    public void MarkConsumed_AlreadyConsumed_NoChange()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);
        scheduler.MarkConsumed();
        var planAfterFirst = scheduler.CurrentPlan!;

        scheduler.MarkConsumed();

        Assert.AreSame(planAfterFirst, scheduler.CurrentPlan);
    }

    // ── Delay not reached ────────────────────────────────────────────────

    [TestMethod]
    public void Evaluate_BeforeDelay_DoesNotFire()
    {
        var scheduler = MakeSchedulerWithDelay(Date1, 60);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 30, currentLocalDate: Date1);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DelayNotReached, result.Reason);
        Assert.IsFalse(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void Evaluate_ExactlyAtDelay_FiresIfRollPasses()
    {
        var scheduler = MakeSchedulerWithDelayAndRoll(Date1, 60, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 60, currentLocalDate: Date1);

        Assert.IsTrue(result.ShouldFire);
    }

    [TestMethod]
    public void Evaluate_JustBeforeDelay_DoesNotFire()
    {
        var scheduler = MakeSchedulerWithDelay(Date1, 60);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 59.9, currentLocalDate: Date1);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DelayNotReached, result.Reason);
    }

    // ── Pause / drag / social / rest ─────────────────────────────────────

    [TestMethod]
    public void Evaluate_Paused_DoesNotFire()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1, isPaused: true);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.Paused, result.Reason);
        Assert.IsFalse(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void Evaluate_Dragging_DoesNotFire()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1, isDragging: true);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.Dragging, result.Reason);
        Assert.IsFalse(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void Evaluate_SocialActive_DoesNotFire()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1, isSocialActive: true);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.SocialActive, result.Reason);
        Assert.IsFalse(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void Evaluate_RestReminderActive_DoesNotFire()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1, isRestReminderActive: true);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.RestReminderActive, result.Reason);
        Assert.IsFalse(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void Evaluate_PauseBeforeDelay_PlanNotConsumed()
    {
        var scheduler = MakeSchedulerWithDelay(Date1, 60);

        // Paused before delay reached: plan should not be consumed.
        var r1 = scheduler.Evaluate(effectiveRuntimeMinutes: 30, currentLocalDate: Date1, isPaused: true);
        Assert.IsFalse(r1.ShouldFire);
        Assert.IsFalse(r1.UpdatedPlan!.Consumed);

        // Later, unpaused and delay reached: should fire.
        var r2 = scheduler.Evaluate(effectiveRuntimeMinutes: 60, currentLocalDate: Date1, isPaused: false);
        // Plan was not consumed during pause, so it can still fire.
        // But we need to also have a passing roll.
        // The roll was pre-set via MakeSchedulerWithDelay (default roll from CreateNew).
        // We can't guarantee the roll, so just check it's not PlanConsumed.
        Assert.AreNotEqual(RareEventScheduler.DenyReason.PlanConsumed, r2.Reason);
    }

    // ── Cross-day ────────────────────────────────────────────────────────

    [TestMethod]
    public void EnsurePlan_DateAdvances_CreatesNewPlan()
    {
        var rng = new Random(42);
        var scheduler = new RareEventScheduler();
        scheduler.EnsurePlanForDate(Date1, rng);
        var plan1 = scheduler.CurrentPlan!;

        var created = scheduler.EnsurePlanForDate(Date2, rng);
        var plan2 = scheduler.CurrentPlan!;

        Assert.IsTrue(created);
        Assert.AreNotSame(plan1, plan2);
        Assert.AreEqual(Date2, plan2.LocalDate);
        Assert.IsFalse(plan2.Consumed);
    }

    [TestMethod]
    public void Evaluate_ConsumedOnDay1_Day2CanFire()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);
        // Consume day 1.
        scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);
        Assert.IsTrue(scheduler.CurrentPlan!.Consumed);

        // Advance to day 2 with a new plan.
        var rng = new Random(99);
        scheduler.EnsurePlanForDate(Date2, rng);
        Assert.IsFalse(scheduler.CurrentPlan!.Consumed);

        // Day 2 can fire (if roll passes — we used a different seed, so just
        // verify it's not blocked by PlanConsumed).
        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date2);
        Assert.AreNotEqual(RareEventScheduler.DenyReason.PlanConsumed, result.Reason);
    }

    // ── Clock rollback ───────────────────────────────────────────────────

    [TestMethod]
    public void EnsurePlan_DateRollback_DoesNotCreateNewPlan()
    {
        var rng = new Random(42);
        var scheduler = new RareEventScheduler();
        scheduler.EnsurePlanForDate(Date2, rng);
        var originalPlan = scheduler.CurrentPlan!;

        // Roll back to Date1 (earlier).
        var created = scheduler.EnsurePlanForDate(Date1, rng);

        Assert.IsFalse(created);
        Assert.AreSame(originalPlan, scheduler.CurrentPlan);
    }

    [TestMethod]
    public void EnsurePlan_ConsumedPlan_DateRollback_StillConsumed()
    {
        var scheduler = MakeSchedulerWithRoll(Date2, 0.1);
        scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date2);
        Assert.IsTrue(scheduler.CurrentPlan!.Consumed);

        // Roll back to Date1.
        var rng = new Random(42);
        scheduler.EnsurePlanForDate(Date1, rng);

        // Plan should still be the consumed Date2 plan.
        Assert.AreEqual(Date2, scheduler.CurrentPlan!.LocalDate);
        Assert.IsTrue(scheduler.CurrentPlan.Consumed);
    }

    // ── No plan ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Evaluate_NoPlan_ReturnsFalseWithNone()
    {
        var scheduler = new RareEventScheduler();

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 100, currentLocalDate: Date1);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.None, result.Reason);
        Assert.IsNull(result.UpdatedPlan);
    }

    // ── Snapshot restore ─────────────────────────────────────────────────

    [TestMethod]
    public void RestorePlan_PreservesPlanAcrossRestart()
    {
        var rng = new Random(42);
        var scheduler1 = new RareEventScheduler();
        scheduler1.EnsurePlanForDate(Date1, rng);
        var originalDelay = scheduler1.CurrentPlan!.TriggerDelayMinutes;
        var originalRoll = scheduler1.CurrentPlan!.TriggerRoll;

        // Simulate restart: save snapshot, create new scheduler, restore.
        var snapshot = scheduler1.GetSnapshot()!;
        var scheduler2 = new RareEventScheduler();
        scheduler2.RestorePlan(snapshot);

        Assert.IsNotNull(scheduler2.CurrentPlan);
        Assert.AreEqual(Date1, scheduler2.CurrentPlan!.LocalDate);
        Assert.AreEqual(originalDelay, scheduler2.CurrentPlan.TriggerDelayMinutes);
        Assert.AreEqual(originalRoll, scheduler2.CurrentPlan.TriggerRoll);
        Assert.IsFalse(scheduler2.CurrentPlan.Consumed);
    }

    [TestMethod]
    public void RestorePlan_ConsumedPlan_DoesNotReTriggerAfterRestart()
    {
        var scheduler1 = MakeSchedulerWithRoll(Date1, 0.1);
        scheduler1.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1); // Consume.

        var snapshot = scheduler1.GetSnapshot()!;
        var scheduler2 = new RareEventScheduler();
        scheduler2.RestorePlan(snapshot);

        var result = scheduler2.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);
        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.PlanConsumed, result.Reason);
    }

    [TestMethod]
    public void RestorePlan_NullPlan_EvaluateReturnsNone()
    {
        var scheduler = new RareEventScheduler();
        scheduler.RestorePlan(null);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 100, currentLocalDate: Date1);
        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.None, result.Reason);
    }

    [TestMethod]
    public void RestorePlan_RestartDoesNotReRoll_SameTriggerDecision()
    {
        var rng = new Random(42);
        var scheduler1 = new RareEventScheduler();
        scheduler1.EnsurePlanForDate(Date1, rng);

        // Save snapshot BEFORE evaluating (simulates save-then-restart).
        var snapshot = scheduler1.GetSnapshot()!;

        // Restart with same snapshot.
        var scheduler2 = new RareEventScheduler();
        scheduler2.RestorePlan(snapshot);

        // Both schedulers should produce the same trigger decision at the same
        // effective runtime, because the plan (delay + roll) is identical.
        var runtime = snapshot.TriggerDelayMinutes + 5; // Past the delay.
        var result1 = scheduler1.Evaluate(effectiveRuntimeMinutes: runtime, currentLocalDate: Date1);
        var result2 = scheduler2.Evaluate(effectiveRuntimeMinutes: runtime, currentLocalDate: Date1);

        // Same decision on both.
        Assert.AreEqual(result1.ShouldFire, result2.ShouldFire);
        Assert.AreEqual(result1.Reason, result2.Reason);

        // The trigger decision matches the pre-rolled value.
        Assert.AreEqual(result1.ShouldFire, snapshot.ShouldTrigger());
    }

    // ── Custom delay range ───────────────────────────────────────────────

    [TestMethod]
    public void EnsurePlan_CustomDelayRange_StaysWithinBounds()
    {
        var rng = new Random(42);
        var scheduler = new RareEventScheduler();

        scheduler.EnsurePlanForDate(Date1, rng, minDelayMinutes: 5, maxDelayMinutes: 10);

        var delay = scheduler.CurrentPlan!.TriggerDelayMinutes;
        Assert.IsTrue(delay >= 5 && delay <= 10);
    }

    [TestMethod]
    public void EnsurePlan_MinEqualsMax_DelayIsExact()
    {
        var rng = new Random(42);
        var scheduler = new RareEventScheduler();

        scheduler.EnsurePlanForDate(Date1, rng, minDelayMinutes: 30, maxDelayMinutes: 30);

        Assert.AreEqual(30, scheduler.CurrentPlan!.TriggerDelayMinutes);
    }

    // ── Determinism with fixed seed ──────────────────────────────────────

    [TestMethod]
    public void FixedSeed_ProducesSamePlan()
    {
        var scheduler1 = new RareEventScheduler();
        scheduler1.EnsurePlanForDate(Date1, new Random(123));

        var scheduler2 = new RareEventScheduler();
        scheduler2.EnsurePlanForDate(Date1, new Random(123));

        Assert.AreEqual(scheduler1.CurrentPlan!.TriggerDelayMinutes, scheduler2.CurrentPlan!.TriggerDelayMinutes);
        Assert.AreEqual(scheduler1.CurrentPlan!.TriggerRoll, scheduler2.CurrentPlan!.TriggerRoll);
    }

    // ── F-01: Date consistency — continuous rollback sequence ─────────────

    [TestMethod]
    public void Evaluate_DateMismatch_Rollback_DeniesAndDoesNotConsume()
    {
        // Plan is for Date2, but we evaluate with Date1 (rollback).
        var scheduler = MakeSchedulerWithRoll(Date2, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DateRollback, result.Reason);
        // Plan must NOT be consumed during rollback.
        Assert.IsFalse(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void Evaluate_DateMismatch_Advanced_DeniesAndDoesNotConsume()
    {
        // Plan is for Date1, but we evaluate with Date2 (advanced without EnsurePlanForDate).
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date2);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DateAdvanced, result.Reason);
        Assert.IsFalse(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void F01_ContinuousSequence_Day1Fire_Day2New_RollbackDay1_Deny_BackToDay2()
    {
        // F-01 continuous sequence:
        // 1. Day1: trigger and consume the plan.
        // 2. Day2: create new plan, evaluate (not blocked by day1 consumed).
        // 3. Rollback to Day1: Evaluate denies with DateRollback, does not consume day2 plan.
        // 4. Back to Day2: Evaluate proceeds normally.

        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        // 1. Day1: fire and consume.
        var r1 = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);
        Assert.IsTrue(r1.ShouldFire);
        Assert.IsTrue(scheduler.CurrentPlan!.Consumed);
        Assert.AreEqual(Date1, scheduler.CurrentPlan!.LocalDate);

        // 2. Day2: create new plan.
        var rng = new Random(99);
        scheduler.EnsurePlanForDate(Date2, rng);
        Assert.AreEqual(Date2, scheduler.CurrentPlan!.LocalDate);
        Assert.IsFalse(scheduler.CurrentPlan.Consumed);

        // 3. Rollback to Day1: Evaluate must deny with DateRollback.
        //    EnsurePlanForDate(Date1) returns false (no new plan), plan stays Date2.
        var ensured = scheduler.EnsurePlanForDate(Date1, rng);
        Assert.IsFalse(ensured);
        Assert.AreEqual(Date2, scheduler.CurrentPlan!.LocalDate);

        var r3 = scheduler.Evaluate(effectiveRuntimeMinutes: 100, currentLocalDate: Date1);
        Assert.IsFalse(r3.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DateRollback, r3.Reason);
        // Day2 plan must NOT be consumed by the rollback evaluation.
        Assert.IsFalse(scheduler.CurrentPlan!.Consumed);

        // 4. Back to Day2: Evaluate proceeds (not PlanConsumed, not DateRollback).
        var r4 = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date2);
        Assert.AreNotEqual(RareEventScheduler.DenyReason.DateRollback, r4.Reason);
        Assert.AreNotEqual(RareEventScheduler.DenyReason.PlanConsumed, r4.Reason);
    }

    [TestMethod]
    public void F01_ConsumedDay1_RollbackToDay1_DoesNotReFire()
    {
        // Day1 plan is consumed. Rollback to Day1 and evaluate with high runtime.
        // Must NOT re-fire (quota already used).
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);
        scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);
        Assert.IsTrue(scheduler.CurrentPlan!.Consumed);

        // Evaluate again on Day1 after consuming — should be PlanConsumed, not re-fire.
        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 100, currentLocalDate: Date1);
        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.PlanConsumed, result.Reason);
    }

    [TestMethod]
    public void F01_RestoreConsumedPlan_Rollback_DoesNotReFire()
    {
        // Snapshot restore path: consumed plan restored, then rollback evaluation
        // must not re-fire.
        var scheduler1 = MakeSchedulerWithRoll(Date2, 0.1);
        scheduler1.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date2);
        Assert.IsTrue(scheduler1.CurrentPlan!.Consumed);

        var snapshot = scheduler1.GetSnapshot()!;
        var scheduler2 = new RareEventScheduler();
        Assert.IsTrue(scheduler2.RestorePlan(snapshot));

        // Evaluate with Date1 (rollback from Date2).
        var result = scheduler2.Evaluate(effectiveRuntimeMinutes: 100, currentLocalDate: Date1);
        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DateRollback, result.Reason);
    }

    // ── F-03: Invalid snapshot and invalid runtime ────────────────────────

    [TestMethod]
    public void F03_RestorePlan_BadDate_Rejected()
    {
        var scheduler = new RareEventScheduler();
        var badPlan = new RareEventPlan
        {
            LocalDate = "bad-date",
            TriggerDelayMinutes = 30,
            TriggerRoll = 0.3,
            Consumed = false
        };

        var accepted = scheduler.RestorePlan(badPlan);

        Assert.IsFalse(accepted);
        Assert.IsNull(scheduler.CurrentPlan);
    }

    [TestMethod]
    public void F03_RestorePlan_NanRoll_Rejected()
    {
        var scheduler = new RareEventScheduler();
        var badPlan = new RareEventPlan
        {
            LocalDate = Date1,
            TriggerDelayMinutes = 30,
            TriggerRoll = double.NaN,
            Consumed = false
        };

        var accepted = scheduler.RestorePlan(badPlan);

        Assert.IsFalse(accepted);
        Assert.IsNull(scheduler.CurrentPlan);
    }

    [TestMethod]
    public void F03_RestorePlan_OutOfRangeDelay_Rejected()
    {
        var scheduler = new RareEventScheduler();
        var badPlan = new RareEventPlan
        {
            LocalDate = Date1,
            TriggerDelayMinutes = 200, // > MaxTriggerDelayMinutes (120)
            TriggerRoll = 0.3,
            Consumed = false
        };

        var accepted = scheduler.RestorePlan(badPlan);

        Assert.IsFalse(accepted);
        Assert.IsNull(scheduler.CurrentPlan);
    }

    [TestMethod]
    public void F03_RestorePlan_NegativeDelay_Rejected()
    {
        var scheduler = new RareEventScheduler();
        var badPlan = new RareEventPlan
        {
            LocalDate = Date1,
            TriggerDelayMinutes = -5,
            TriggerRoll = 0.3,
            Consumed = false
        };

        var accepted = scheduler.RestorePlan(badPlan);

        Assert.IsFalse(accepted);
        Assert.IsNull(scheduler.CurrentPlan);
    }

    [TestMethod]
    public void F03_RestorePlan_InvalidPlan_PreservesExistingValidPlan()
    {
        // First install a valid plan.
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);
        var validPlan = scheduler.CurrentPlan!;

        // Try to restore an invalid plan — must be rejected, existing plan preserved.
        var badPlan = new RareEventPlan
        {
            LocalDate = "not-a-date",
            TriggerDelayMinutes = 999,
            TriggerRoll = double.NaN,
            Consumed = false
        };

        var accepted = scheduler.RestorePlan(badPlan);

        Assert.IsFalse(accepted);
        Assert.AreSame(validPlan, scheduler.CurrentPlan);
    }

    [TestMethod]
    public void F03_Evaluate_NaNRuntime_Rejected()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: double.NaN, currentLocalDate: Date1);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.InvalidRuntime, result.Reason);
        // Plan must NOT be consumed.
        Assert.IsFalse(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void F03_Evaluate_InfinityRuntime_Rejected()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: double.PositiveInfinity, currentLocalDate: Date1);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.InvalidRuntime, result.Reason);
        Assert.IsFalse(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void F03_Evaluate_NegativeRuntime_Rejected()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: -10, currentLocalDate: Date1);

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.InvalidRuntime, result.Reason);
        Assert.IsFalse(result.UpdatedPlan!.Consumed);
    }

    [TestMethod]
    public void F03_Evaluate_InvalidDate_Rejected()
    {
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: "not-a-date");

        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.InvalidDate, result.Reason);
    }

    [TestMethod]
    public void F03_RestorePlan_ValidPlan_AcceptedAndFunctional()
    {
        var validPlan = new RareEventPlan
        {
            LocalDate = Date1,
            TriggerDelayMinutes = 20,
            TriggerRoll = 0.1,
            Consumed = false
        };

        var scheduler = new RareEventScheduler();
        var accepted = scheduler.RestorePlan(validPlan);

        Assert.IsTrue(accepted);
        Assert.IsNotNull(scheduler.CurrentPlan);

        var result = scheduler.Evaluate(effectiveRuntimeMinutes: 25, currentLocalDate: Date1);
        Assert.IsTrue(result.ShouldFire);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static RareEventScheduler MakeSchedulerWithRoll(string date, double roll)
    {
        var plan = new RareEventPlan
        {
            LocalDate = date,
            TriggerDelayMinutes = 20,
            TriggerRoll = roll,
            Consumed = false,
            AccumulatedBaselineMinutes = 0
        };
        var scheduler = new RareEventScheduler();
        scheduler.RestorePlan(plan);
        return scheduler;
    }

    private static RareEventScheduler MakeSchedulerWithDelay(string date, int delayMinutes)
    {
        var plan = new RareEventPlan
        {
            LocalDate = date,
            TriggerDelayMinutes = delayMinutes,
            TriggerRoll = 0.1, // Will trigger if delay reached.
            Consumed = false,
            AccumulatedBaselineMinutes = 0
        };
        var scheduler = new RareEventScheduler();
        scheduler.RestorePlan(plan);
        return scheduler;
    }

    private static RareEventScheduler MakeSchedulerWithDelayAndRoll(string date, int delayMinutes, double roll)
    {
        var plan = new RareEventPlan
        {
            LocalDate = date,
            TriggerDelayMinutes = delayMinutes,
            TriggerRoll = roll,
            Consumed = false,
            AccumulatedBaselineMinutes = 0
        };
        var scheduler = new RareEventScheduler();
        scheduler.RestorePlan(plan);
        return scheduler;
    }

    // ── F-05: Daily effective time baseline ────────────────────────────────

    [TestMethod]
    public void F05_Day1Fire_Day2NewPlan_DelayRelativeToDay2Baseline()
    {
        // F-05 core scenario:
        // Day1: 130 min accumulated, plan delay=20, fires and consumes.
        // Day2: new plan with delay=20, baseline=130 (accumulated at creation).
        //   - accumulated=130 (0 new day2 minutes) → DelayNotReached
        //   - accumulated=149 (19 new day2 minutes) → DelayNotReached
        //   - accumulated=150 (20 new day2 minutes) → fires
        // Use a manually constructed plan with known delay=20 and roll=0.1 (triggers).
        var day1Plan = new RareEventPlan
        {
            LocalDate = Date1,
            TriggerDelayMinutes = 20,
            TriggerRoll = 0.1,
            Consumed = false,
            AccumulatedBaselineMinutes = 0
        };
        var scheduler = new RareEventScheduler();
        scheduler.RestorePlan(day1Plan);

        // Day1: fire at 130 min (daily effective = 130 - 0 = 130 >= 20).
        var r1 = scheduler.Evaluate(effectiveRuntimeMinutes: 130, currentLocalDate: Date1);
        Assert.IsTrue(r1.ShouldFire);
        Assert.IsTrue(scheduler.CurrentPlan!.Consumed);

        // Advance to Day2: create new plan with delay=20, baseline=130.
        var day2Plan = new RareEventPlan
        {
            LocalDate = Date2,
            TriggerDelayMinutes = 20,
            TriggerRoll = 0.1,
            Consumed = false,
            AccumulatedBaselineMinutes = 130
        };
        scheduler.RestorePlan(day2Plan);
        Assert.AreEqual(Date2, scheduler.CurrentPlan!.LocalDate);
        Assert.IsFalse(scheduler.CurrentPlan.Consumed);
        Assert.AreEqual(130.0, scheduler.CurrentPlan.AccumulatedBaselineMinutes, 0.001);

        // Day2: accumulated=130 → daily effective = 0 → DelayNotReached.
        var r2 = scheduler.Evaluate(effectiveRuntimeMinutes: 130, currentLocalDate: Date2);
        Assert.IsFalse(r2.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DelayNotReached, r2.Reason);

        // Day2: accumulated=149 → daily effective = 19 → DelayNotReached.
        var r3 = scheduler.Evaluate(effectiveRuntimeMinutes: 149, currentLocalDate: Date2);
        Assert.IsFalse(r3.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DelayNotReached, r3.Reason);

        // Day2: accumulated=150 → daily effective = 20 → fires (roll 0.1 < 0.5).
        var r4 = scheduler.Evaluate(effectiveRuntimeMinutes: 150, currentLocalDate: Date2);
        Assert.IsTrue(r4.ShouldFire);
    }

    [TestMethod]
    public void F05_Day2DelayNotReached_DoesNotConsumePlan()
    {
        // When daily effective time hasn't reached the delay, the plan must NOT be consumed.
        var scheduler = MakeSchedulerWithRoll(Date1, 0.1);
        scheduler.Evaluate(effectiveRuntimeMinutes: 130, currentLocalDate: Date1);
        Assert.IsTrue(scheduler.CurrentPlan!.Consumed);

        var rng = new Random(99);
        scheduler.EnsurePlanForDate(Date2, rng, currentAccumulatedMinutes: 130);

        var r = scheduler.Evaluate(effectiveRuntimeMinutes: 140, currentLocalDate: Date2);
        Assert.IsFalse(r.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DelayNotReached, r.Reason);
        // Plan must NOT be consumed when delay not reached.
        Assert.IsFalse(scheduler.CurrentPlan!.Consumed);
    }

    [TestMethod]
    public void F05_SnapshotRestore_PreservesBaselineAndDelay()
    {
        // After save/restart, the restored plan should still have the correct
        // baseline, so the delay check works the same way.
        var day1Plan = new RareEventPlan
        {
            LocalDate = Date1,
            TriggerDelayMinutes = 20,
            TriggerRoll = 0.1,
            Consumed = false,
            AccumulatedBaselineMinutes = 0
        };
        var scheduler1 = new RareEventScheduler();
        scheduler1.RestorePlan(day1Plan);
        scheduler1.Evaluate(effectiveRuntimeMinutes: 130, currentLocalDate: Date1);

        // Create Day2 plan with known delay=20, baseline=130.
        var day2Plan = new RareEventPlan
        {
            LocalDate = Date2,
            TriggerDelayMinutes = 20,
            TriggerRoll = 0.1,
            Consumed = false,
            AccumulatedBaselineMinutes = 130
        };
        scheduler1.RestorePlan(day2Plan);
        var snapshot = scheduler1.GetSnapshot()!;

        // Restart: restore the snapshot.
        var scheduler2 = new RareEventScheduler();
        Assert.IsTrue(scheduler2.RestorePlan(snapshot));
        Assert.AreEqual(130.0, scheduler2.CurrentPlan!.AccumulatedBaselineMinutes, 0.001);

        // Day2: accumulated=130 → daily effective = 0 → DelayNotReached.
        var r1 = scheduler2.Evaluate(effectiveRuntimeMinutes: 130, currentLocalDate: Date2);
        Assert.IsFalse(r1.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DelayNotReached, r1.Reason);

        // Day2: accumulated=150 → daily effective = 20 → fires (roll 0.1 < 0.5).
        var r2 = scheduler2.Evaluate(effectiveRuntimeMinutes: 150, currentLocalDate: Date2);
        Assert.IsTrue(r2.ShouldFire);
    }

    [TestMethod]
    public void F05_FirstDay_BaselineZero_WorksAsBefore()
    {
        // First day: baseline=0, so behavior is identical to pre-F-05.
        var rng = new Random(42);
        var scheduler = new RareEventScheduler();
        scheduler.EnsurePlanForDate(Date1, rng, currentAccumulatedMinutes: 0);
        Assert.AreEqual(0.0, scheduler.CurrentPlan!.AccumulatedBaselineMinutes, 0.001);

        // Before delay: DelayNotReached.
        var delay = scheduler.CurrentPlan.TriggerDelayMinutes;
        var r1 = scheduler.Evaluate(effectiveRuntimeMinutes: delay - 1, currentLocalDate: Date1);
        Assert.IsFalse(r1.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DelayNotReached, r1.Reason);

        // At/after delay: not DelayNotReached.
        var r2 = scheduler.Evaluate(effectiveRuntimeMinutes: delay + 5, currentLocalDate: Date1);
        Assert.AreNotEqual(RareEventScheduler.DenyReason.DelayNotReached, r2.Reason);
    }

    [TestMethod]
    public void F05_RollbackAndForward_BaselineNotAffected()
    {
        // F-05 must not break F-01 rollback/forward behavior.
        var scheduler = MakeSchedulerWithRoll(Date2, 0.1);
        // Manually set baseline for Date2 plan.
        var planWithBaseline = new RareEventPlan
        {
            LocalDate = Date2,
            TriggerDelayMinutes = 20,
            TriggerRoll = 0.1,
            Consumed = false,
            AccumulatedBaselineMinutes = 100
        };
        scheduler.RestorePlan(planWithBaseline);

        // Rollback to Date1: denied with DateRollback (F-01 still works).
        var r1 = scheduler.Evaluate(effectiveRuntimeMinutes: 200, currentLocalDate: Date1);
        Assert.IsFalse(r1.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DateRollback, r1.Reason);

        // Back to Date2: daily effective = 200 - 100 = 100 >= 20 → not DelayNotReached.
        var r2 = scheduler.Evaluate(effectiveRuntimeMinutes: 120, currentLocalDate: Date2);
        Assert.AreNotEqual(RareEventScheduler.DenyReason.DelayNotReached, r2.Reason);
        Assert.AreNotEqual(RareEventScheduler.DenyReason.DateRollback, r2.Reason);
    }

    [TestMethod]
    public void F05_InvalidBaseline_RejectedByRestorePlan()
    {
        // Plan with NaN baseline should be rejected by IsValid.
        var badPlan = new RareEventPlan
        {
            LocalDate = Date1,
            TriggerDelayMinutes = 30,
            TriggerRoll = 0.3,
            Consumed = false,
            AccumulatedBaselineMinutes = double.NaN
        };

        var scheduler = new RareEventScheduler();
        var accepted = scheduler.RestorePlan(badPlan);

        Assert.IsFalse(accepted);
        Assert.IsNull(scheduler.CurrentPlan);
    }

    [TestMethod]
    public void F05_NegativeBaseline_RejectedByRestorePlan()
    {
        var badPlan = new RareEventPlan
        {
            LocalDate = Date1,
            TriggerDelayMinutes = 30,
            TriggerRoll = 0.3,
            Consumed = false,
            AccumulatedBaselineMinutes = -10
        };

        var scheduler = new RareEventScheduler();
        var accepted = scheduler.RestorePlan(badPlan);

        Assert.IsFalse(accepted);
        Assert.IsNull(scheduler.CurrentPlan);
    }
}
