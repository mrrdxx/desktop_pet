using DesktopPet.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// PeekScheduler unit tests (PRODUCT R-03).
/// Uses controlled Random seeds and synthetic time deltas — no real waiting.
/// </summary>
[TestClass]
public class PeekSchedulerTests
{
    private const string Date1 = "2026-10-24";
    private const string Date2 = "2026-10-25";

    // ── Plan creation ──────────────────────────────────────────────────

    [TestMethod]
    public void FirstTick_CreatesPlan_WithCandidateIn20To120Range()
    {
        var scheduler = new PeekScheduler(new Random(42));
        var result = scheduler.Tick(Date1, deltaMs: 1000, isPaused: false, isInterrupted: false, nowMs: 1000);

        Assert.AreEqual(PeekTickResult.NoAction, result);
        Assert.AreEqual(Date1, scheduler.EventDate);
        Assert.IsTrue(scheduler.CandidateMinutes >= PeekScheduler.MinCandidateMinutes);
        Assert.IsTrue(scheduler.CandidateMinutes <= PeekScheduler.MaxCandidateMinutes);
        Assert.IsFalse(scheduler.HasAttempted);
        Assert.IsFalse(scheduler.HasTriggered);
    }

    [TestMethod]
    public void SameSeed_ProducesSameCandidate()
    {
        var s1 = new PeekScheduler(new Random(123));
        s1.Tick(Date1, 1000, false, false, 1000);
        var c1 = s1.CandidateMinutes;

        var s2 = new PeekScheduler(new Random(123));
        s2.Tick(Date1, 1000, false, false, 1000);
        var c2 = s2.CandidateMinutes;

        Assert.AreEqual(c1, c2);
    }

    [TestMethod]
    public void DifferentSeed_ProducesDifferentCandidate()
    {
        // Try multiple seeds to find a difference (random could theoretically match).
        var candidates = new HashSet<int>();
        for (int seed = 0; seed < 20; seed++)
        {
            var s = new PeekScheduler(new Random(seed));
            s.Tick(Date1, 1000, false, false, 1000);
            candidates.Add(s.CandidateMinutes);
        }
        Assert.IsTrue(candidates.Count > 1, "Different seeds should produce different candidates.");
    }

    // ── Effective run time accumulation ────────────────────────────────

    [TestMethod]
    public void PausedTime_DoesNotAccumulate()
    {
        var scheduler = new PeekScheduler(new Random(1));
        scheduler.Tick(Date1, 1000, false, false, 1000);

        // Simulate 10 minutes of paused time.
        scheduler.Tick(Date1, 10 * 60_000.0, true, false, 10 * 60_000.0 + 1000);

        Assert.IsTrue(scheduler.EffectiveRunMs < 60_000, "Paused time should not accumulate as effective run time.");
        Assert.AreEqual(1000, scheduler.EffectiveRunMs, 0.001);
    }

    [TestMethod]
    public void ActiveTime_AccumulatesCorrectly()
    {
        var scheduler = new PeekScheduler(new Random(1));
        scheduler.Tick(Date1, 1000, false, false, 1000);

        // Accumulate 5 minutes of active time in 1-minute increments.
        for (int i = 0; i < 5; i++)
        {
            scheduler.Tick(Date1, 60_000.0, false, false, (i + 2) * 60_000.0);
        }

        Assert.AreEqual(5 * 60_000.0 + 1000, scheduler.EffectiveRunMs, 0.001);
    }

    // ── Attempt and trigger ────────────────────────────────────────────

    [TestMethod]
    public void ReachingCandidate_AttemptsProbabilityCheck()
    {
        // Use a seed that produces a candidate we can reach quickly.
        // We'll find a seed where candidate is 20 (minimum).
        int seed = FindSeedWithCandidate(20);
        var scheduler = new PeekScheduler(new Random(seed));
        scheduler.Tick(Date1, 1000, false, false, 1000);

        // Accumulate exactly 20 minutes of effective run time.
        var result = scheduler.Tick(Date1, 20 * 60_000.0, false, false, 20 * 60_000.0 + 1000);

        Assert.IsTrue(scheduler.HasAttempted, "Should have attempted when effective run reaches candidate.");
        // Result depends on probability — either NoAction (failed) or PeekStarted (passed).
        Assert.IsTrue(result == PeekTickResult.NoAction || result == PeekTickResult.PeekStarted);
    }

    [TestMethod]
    public void ProbabilityCheck_50Percent_FiresAboutHalfTheTime()
    {
        // Run 1000 trials with different seeds to verify ~50% trigger rate.
        int fireCount = 0;
        int totalAttempts = 0;

        for (int seed = 0; seed < 1000; seed++)
        {
            var scheduler = new PeekScheduler(new Random(seed));
            scheduler.Tick(Date1, 1000, false, false, 1000);
            var candidate = scheduler.CandidateMinutes;

            // Jump to candidate time.
            var result = scheduler.Tick(Date1, candidate * 60_000.0, false, false, candidate * 60_000.0 + 1000);

            if (scheduler.HasAttempted)
            {
                totalAttempts++;
                if (result == PeekTickResult.PeekStarted)
                    fireCount++;
            }
        }

        Assert.AreEqual(1000, totalAttempts, "All seeds should have attempted.");
        // With 1000 trials and 50% probability, expect 400-600 fires.
        Assert.IsTrue(fireCount >= 400 && fireCount <= 600,
            $"Expected ~500 fires, got {fireCount} out of 1000.");
    }

    [TestMethod]
    public void TriggeredPeek_DoesNotRepeatSameDay()
    {
        int seed = FindSeedThatTriggers();
        var scheduler = new PeekScheduler(new Random(seed));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var candidate = scheduler.CandidateMinutes;

        // Reach candidate and trigger.
        var result1 = scheduler.Tick(Date1, candidate * 60_000.0, false, false, candidate * 60_000.0 + 1000);
        Assert.AreEqual(PeekTickResult.PeekStarted, result1);

        // Let peek complete.
        var result2 = scheduler.Tick(Date1, 100, false, false, candidate * 60_000.0 + 1000 + PeekScheduler.PeekDurationMs);
        Assert.AreEqual(PeekTickResult.PeekCompleted, result2);
        Assert.IsTrue(scheduler.HasTriggered);

        // More ticks should not trigger again.
        var result3 = scheduler.Tick(Date1, 60_000.0, false, false, candidate * 60_000.0 + 2000 + PeekScheduler.PeekDurationMs);
        Assert.AreEqual(PeekTickResult.NoAction, result3);
    }

    // ── Interrupt and defer ────────────────────────────────────────────

    [TestMethod]
    public void InterruptedPeek_EndsAndMarksTriggered()
    {
        int seed = FindSeedThatTriggers();
        var scheduler = new PeekScheduler(new Random(seed));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var candidate = scheduler.CandidateMinutes;

        // Trigger peek.
        var result1 = scheduler.Tick(Date1, candidate * 60_000.0, false, false, candidate * 60_000.0 + 1000);
        Assert.AreEqual(PeekTickResult.PeekStarted, result1);
        Assert.IsTrue(scheduler.IsPeeking);

        // Interrupt with drag.
        var result2 = scheduler.Tick(Date1, 100, false, true, candidate * 60_000.0 + 1100);
        Assert.AreEqual(PeekTickResult.PeekInterrupted, result2);
        Assert.IsFalse(scheduler.IsPeeking);
        Assert.IsTrue(scheduler.HasTriggered);
    }

    [TestMethod]
    public void ProbabilityPasses_ButInterrupted_DefersUntilIdle()
    {
        // Find a seed where probability passes.
        int seed = FindSeedThatTriggers();
        var scheduler = new PeekScheduler(new Random(seed));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var candidate = scheduler.CandidateMinutes;

        // Reach candidate while interrupted — should attempt but not fire.
        var result1 = scheduler.Tick(Date1, candidate * 60_000.0, false, true, candidate * 60_000.0 + 1000);
        Assert.IsTrue(scheduler.HasAttempted);
        Assert.IsTrue(scheduler.ShouldFire);
        Assert.AreEqual(PeekTickResult.NoAction, result1);

        // Now become idle — should fire.
        var result2 = scheduler.Tick(Date1, 100, false, false, candidate * 60_000.0 + 1100);
        Assert.AreEqual(PeekTickResult.PeekStarted, result2);
    }

    [TestMethod]
    public void Paused_DoesNotFire()
    {
        int seed = FindSeedThatTriggers();
        var scheduler = new PeekScheduler(new Random(seed));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var candidate = scheduler.CandidateMinutes;

        // Accumulate time up to just before candidate while active.
        scheduler.Tick(Date1, (candidate - 1) * 60_000.0, false, false, (candidate - 1) * 60_000.0 + 1000);
        Assert.IsFalse(scheduler.HasAttempted, "Should not have attempted before reaching candidate.");

        // Now pause — remaining time should not accumulate, so candidate is never reached.
        for (int i = 0; i < 10; i++)
        {
            var result = scheduler.Tick(Date1, 60_000.0, true, false, (candidate + i) * 60_000.0 + 1000);
            Assert.AreEqual(PeekTickResult.NoAction, result);
        }
        Assert.IsFalse(scheduler.HasAttempted, "Paused time should not reach candidate.");
    }

    // ── Date change ────────────────────────────────────────────────────

    [TestMethod]
    public void NewDay_CreatesNewPlan()
    {
        var scheduler = new PeekScheduler(new Random(42));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var oldCandidate = scheduler.CandidateMinutes;
        Assert.AreEqual(Date1, scheduler.EventDate);

        // Move to next day.
        var result = scheduler.Tick(Date2, 1000, false, false, 1000);
        Assert.AreEqual(Date2, scheduler.EventDate);
        Assert.IsFalse(scheduler.HasAttempted);
        Assert.IsFalse(scheduler.HasTriggered);
        Assert.AreEqual(1000, scheduler.EffectiveRunMs, 0.001);
    }

    [TestMethod]
    public void ClockRollback_DoesNotCreateNewPlan()
    {
        var scheduler = new PeekScheduler(new Random(42));
        scheduler.Tick(Date2, 1000, false, false, 1000);
        Assert.AreEqual(Date2, scheduler.EventDate);
        var originalCandidate = scheduler.CandidateMinutes;

        // Roll back to previous day.
        var result = scheduler.Tick(Date1, 1000, false, false, 2000);
        Assert.AreEqual(Date2, scheduler.EventDate, "Clock rollback should not change the event date.");
        Assert.AreEqual(originalCandidate, scheduler.CandidateMinutes, "Clock rollback should not re-roll candidate.");
    }

    [TestMethod]
    public void TriggeredToday_NewDay_CanTriggerAgain()
    {
        int seed = FindSeedThatTriggers();
        var scheduler = new PeekScheduler(new Random(seed));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var candidate = scheduler.CandidateMinutes;

        // Trigger and complete.
        scheduler.Tick(Date1, candidate * 60_000.0, false, false, candidate * 60_000.0 + 1000);
        scheduler.Tick(Date1, 100, false, false, candidate * 60_000.0 + 1000 + PeekScheduler.PeekDurationMs);
        Assert.IsTrue(scheduler.HasTriggered);

        // New day — should be able to trigger again.
        var result = scheduler.Tick(Date2, 1000, false, false, 1000);
        Assert.IsFalse(scheduler.HasTriggered, "New day should reset triggered state.");
        Assert.IsFalse(scheduler.HasAttempted);
    }

    // ── Persistence / restore ──────────────────────────────────────────

    [TestMethod]
    public void Restore_PreservesPlanAcrossRestart()
    {
        var scheduler = new PeekScheduler(new Random(42));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var candidate = scheduler.CandidateMinutes;

        // Simulate restart: create new scheduler and restore state.
        var restored = new PeekScheduler(new Random(999));
        restored.Restore(Date1, candidate, hasAttempted: false, shouldFire: false,
            hasTriggered: false, effectiveRunMs: 5000);

        Assert.AreEqual(Date1, restored.EventDate);
        Assert.AreEqual(candidate, restored.CandidateMinutes);
        Assert.IsFalse(restored.HasAttempted);
        Assert.IsFalse(restored.HasTriggered);
        Assert.AreEqual(5000, restored.EffectiveRunMs, 0.001);
    }

    [TestMethod]
    public void Restore_DoesNotReRoll_OnSameDay()
    {
        var scheduler = new PeekScheduler(new Random(42));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var candidate = scheduler.CandidateMinutes;

        // Simulate restart with a different seed — should NOT re-roll.
        var restored = new PeekScheduler(new Random(999));
        restored.Restore(Date1, candidate, false, false, false, 5000);

        // Tick on the same day — should not create a new plan.
        restored.Tick(Date1, 1000, false, false, 6000);
        Assert.AreEqual(candidate, restored.CandidateMinutes, "Restart should not re-roll candidate.");
    }

    [TestMethod]
    public void Restore_AlreadyTriggered_DoesNotTriggerAgain()
    {
        var scheduler = new PeekScheduler(new Random(42));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var candidate = scheduler.CandidateMinutes;

        // Simulate restart after trigger was already consumed.
        var restored = new PeekScheduler(new Random(999));
        restored.Restore(Date1, candidate, hasAttempted: true, shouldFire: true,
            hasTriggered: true, effectiveRunMs: candidate * 60_000.0);

        // Tick — should not trigger again.
        var result = restored.Tick(Date1, 1000, false, false, candidate * 60_000.0 + 1000);
        Assert.AreEqual(PeekTickResult.NoAction, result);
        Assert.IsTrue(restored.HasTriggered);
    }

    [TestMethod]
    public void Restore_NeverRestoresMidPeek()
    {
        var restored = new PeekScheduler(new Random(999));
        restored.Restore(Date1, 30, true, true, false, 30 * 60_000.0);
        Assert.IsFalse(restored.IsPeeking, "IsPeeking should never be restored as true.");
    }

    // ── No carry-over ──────────────────────────────────────────────────

    [TestMethod]
    public void NoSuitableWindow_AllDay_DoesNotCarryOver()
    {
        int seed = FindSeedThatTriggers();
        var scheduler = new PeekScheduler(new Random(seed));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var candidate = scheduler.CandidateMinutes;

        // Reach candidate while interrupted all day.
        scheduler.Tick(Date1, candidate * 60_000.0, false, true, candidate * 60_000.0 + 1000);
        Assert.IsTrue(scheduler.HasAttempted);
        Assert.IsTrue(scheduler.ShouldFire);

        // Stay interrupted for the rest of the day.
        for (int i = 0; i < 100; i++)
        {
            scheduler.Tick(Date1, 60_000.0, false, true, (candidate + i + 2) * 60_000.0);
        }
        Assert.IsFalse(scheduler.HasTriggered, "Should not have triggered while interrupted all day.");

        // New day — should start fresh, old deferred peek is lost.
        scheduler.Tick(Date2, 1000, false, false, 1000);
        Assert.IsFalse(scheduler.HasAttempted, "New day should reset attempt state.");
        Assert.IsFalse(scheduler.HasTriggered);
    }

    // ── Peek duration ──────────────────────────────────────────────────

    [TestMethod]
    public void PeekCompletes_AfterDuration()
    {
        int seed = FindSeedThatTriggers();
        var scheduler = new PeekScheduler(new Random(seed));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var candidate = scheduler.CandidateMinutes;

        // Trigger peek.
        var startMs = candidate * 60_000.0 + 1000;
        scheduler.Tick(Date1, candidate * 60_000.0, false, false, startMs);
        Assert.IsTrue(scheduler.IsPeeking);

        // Just before duration — still peeking.
        var result1 = scheduler.Tick(Date1, 100, false, false, startMs + PeekScheduler.PeekDurationMs - 100);
        Assert.AreEqual(PeekTickResult.PeekInProgress, result1);
        Assert.IsTrue(scheduler.IsPeeking);

        // At duration — completed.
        var result2 = scheduler.Tick(Date1, 100, false, false, startMs + PeekScheduler.PeekDurationMs);
        Assert.AreEqual(PeekTickResult.PeekCompleted, result2);
        Assert.IsFalse(scheduler.IsPeeking);
        Assert.IsTrue(scheduler.HasTriggered);
    }

    [TestMethod]
    public void EndPeek_ManualEnd_MarksTriggered()
    {
        int seed = FindSeedThatTriggers();
        var scheduler = new PeekScheduler(new Random(seed));
        scheduler.Tick(Date1, 1000, false, false, 1000);
        var candidate = scheduler.CandidateMinutes;

        scheduler.Tick(Date1, candidate * 60_000.0, false, false, candidate * 60_000.0 + 1000);
        Assert.IsTrue(scheduler.IsPeeking);

        scheduler.EndPeek();
        Assert.IsFalse(scheduler.IsPeeking);
        Assert.IsTrue(scheduler.HasTriggered);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static int FindSeedWithCandidate(int targetCandidate)
    {
        for (int seed = 0; seed < 10000; seed++)
        {
            var s = new PeekScheduler(new Random(seed));
            s.Tick(Date1, 1000, false, false, 1000);
            if (s.CandidateMinutes == targetCandidate)
                return seed;
        }
        Assert.Fail($"Could not find seed with candidate {targetCandidate}.");
        return -1;
    }

    private static int FindSeedThatTriggers()
    {
        for (int seed = 0; seed < 10000; seed++)
        {
            var s = new PeekScheduler(new Random(seed));
            s.Tick(Date1, 1000, false, false, 1000);
            var candidate = s.CandidateMinutes;
            var result = s.Tick(Date1, candidate * 60_000.0, false, false, candidate * 60_000.0 + 1000);
            if (result == PeekTickResult.PeekStarted)
                return seed;
        }
        Assert.Fail("Could not find a seed that triggers.");
        return -1;
    }
}
