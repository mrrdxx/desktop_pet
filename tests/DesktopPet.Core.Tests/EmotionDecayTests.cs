using DesktopPet.Core.Emotion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// EmotionDecay half-life and clamping tests.
/// Formulas:
///   Affinity: newValue = 50 + (value - 50) * 0.5^(elapsedDays / 7)
///   Grievance: newValue = 50 + (value - 50) * 0.5^(elapsedHours / 12)
/// </summary>
[TestClass]
public class EmotionDecayTests
{
    private const double Tolerance = 1.0; // int rounding tolerance

    // ── Affinity decay ─────────────────────────────────────────────────

    [TestMethod]
    public void DecayAffinity_ZeroElapsed_ReturnsSameValue()
    {
        var result = EmotionDecay.DecayAffinity(80, 0);
        Assert.AreEqual(80, result);
    }

    [TestMethod]
    public void DecayAffinity_AtNeutral_StaysNeutral()
    {
        var result = EmotionDecay.DecayAffinity(EmotionState.Neutral, 3.5);
        Assert.AreEqual(EmotionState.Neutral, result);
    }

    [TestMethod]
    public void DecayAffinity_OneHalfLife_HalvesDeviation()
    {
        // After 7 days (one half-life), deviation from 50 should halve.
        // 80 -> 50 + (80-50)*0.5 = 50 + 15 = 65
        var result = EmotionDecay.DecayAffinity(80, EmotionDecay.AffinityHalfLifeDays);
        Assert.AreEqual(65, result);
    }

    [TestMethod]
    public void DecayAffinity_TwoHalfLives_QuartersDeviation()
    {
        // After 14 days (two half-lives), deviation quarters.
        // 80 -> 50 + 30*0.25 = 50 + 7.5 = 57.5 -> rounds to 58
        var result = EmotionDecay.DecayAffinity(80, EmotionDecay.AffinityHalfLifeDays * 2);
        Assert.AreEqual(58, result);
    }

    [TestMethod]
    public void DecayAffinity_BelowNeutral_RisesTowardNeutral()
    {
        // 20 -> 50 + (20-50)*0.5 = 50 - 15 = 35
        var result = EmotionDecay.DecayAffinity(20, EmotionDecay.AffinityHalfLifeDays);
        Assert.AreEqual(35, result);
    }

    [TestMethod]
    public void DecayAffinity_NegativeElapsed_TreatedAsZero()
    {
        var result = EmotionDecay.DecayAffinity(80, -5);
        Assert.AreEqual(80, result);
    }

    [TestMethod]
    public void DecayAffinity_ExceedsMaxDecay_ClampedTo30Days()
    {
        // 60 days should be clamped to 30 days.
        // At 30 days: 80 -> 50 + 30 * 0.5^(30/7) = 50 + 30 * 0.5^4.286
        var at30 = EmotionDecay.DecayAffinity(80, EmotionDecay.MaxDecayDays);
        var at60 = EmotionDecay.DecayAffinity(80, 60);
        Assert.AreEqual(at30, at60);
    }

    // ── Grievance decay ────────────────────────────────────────────────

    [TestMethod]
    public void DecayGrievance_ZeroElapsed_ReturnsSameValue()
    {
        var result = EmotionDecay.DecayGrievance(90, 0);
        Assert.AreEqual(90, result);
    }

    [TestMethod]
    public void DecayGrievance_AtNeutral_StaysNeutral()
    {
        var result = EmotionDecay.DecayGrievance(EmotionState.Neutral, 6);
        Assert.AreEqual(EmotionState.Neutral, result);
    }

    [TestMethod]
    public void DecayGrievance_OneHalfLife_HalvesDeviation()
    {
        // After 12 hours (one half-life): 90 -> 50 + 40*0.5 = 70
        var result = EmotionDecay.DecayGrievance(90, EmotionDecay.GrievanceHalfLifeHours);
        Assert.AreEqual(70, result);
    }

    [TestMethod]
    public void DecayGrievance_BelowNeutral_RisesTowardNeutral()
    {
        // 30 -> 50 + (30-50)*0.5 = 50 - 10 = 40
        var result = EmotionDecay.DecayGrievance(30, EmotionDecay.GrievanceHalfLifeHours);
        Assert.AreEqual(40, result);
    }

    [TestMethod]
    public void DecayGrievance_NegativeElapsed_TreatedAsZero()
    {
        var result = EmotionDecay.DecayGrievance(85, -3);
        Assert.AreEqual(85, result);
    }

    [TestMethod]
    public void DecayGrievance_ExceedsMaxDecay_ClampedTo30Days()
    {
        // 60 days = 1440 hours, should clamp to 30 days = 720 hours.
        var at720 = EmotionDecay.DecayGrievance(90, 720);
        var at1440 = EmotionDecay.DecayGrievance(90, 1440);
        Assert.AreEqual(at720, at1440);
    }

    // ── Expression-then-decay invariant ────────────────────────────────

    [TestMethod]
    public void DecayGrievance_AfterExpressionAtNeutral_NoChange()
    {
        // After expression, grievance is set to 50 (neutral).
        // Decay of 50 toward 50 should stay 50 — no upward drift.
        var result = EmotionDecay.DecayGrievance(EmotionState.Neutral, 24);
        Assert.AreEqual(EmotionState.Neutral, result);
    }

    [TestMethod]
    public void DecayAffinity_AfterExpressionAtNeutral_NoChange()
    {
        var result = EmotionDecay.DecayAffinity(EmotionState.Neutral, 10);
        Assert.AreEqual(EmotionState.Neutral, result);
    }

    // ── Clock rollback ─────────────────────────────────────────────────

    [TestMethod]
    public void DecayAffinity_NegativeElapsed_DoesNotIncreaseValue()
    {
        // Clock rollback should not increase the emotion value.
        var result = EmotionDecay.DecayAffinity(60, -100);
        Assert.AreEqual(60, result);
    }

    [TestMethod]
    public void DecayGrievance_NegativeElapsed_DoesNotIncreaseValue()
    {
        var result = EmotionDecay.DecayGrievance(70, -50);
        Assert.AreEqual(70, result);
    }

    // ── G-01: Float precision and step-wise accumulation ───────────────

    [TestMethod]
    public void DecayAffinityFloat_PreservesPrecision()
    {
        // Small elapsed time that would round to no change in int,
        // but float should show a tiny decrease.
        var result = EmotionDecay.DecayAffinityFloat(80.0, 0.001); // ~1.44 minutes
        Assert.IsTrue(result < 80.0, "Float decay should show decrease even for tiny intervals.");
        Assert.IsTrue(result > 79.99, "Decrease should be very small for 0.001 days.");
    }

    [TestMethod]
    public void DecayGrievanceFloat_PreservesPrecision()
    {
        var result = EmotionDecay.DecayGrievanceFloat(90.0, 0.01); // ~36 seconds
        Assert.IsTrue(result < 90.0, "Float decay should show decrease even for tiny intervals.");
        Assert.IsTrue(result > 89.9, "Decrease should be very small for 0.01 hours.");
    }

    [TestMethod]
    public void Accumulator_ManySmallSteps_MatchOneBigStep_Affinity()
    {
        // G-01 core invariant: many small decay steps should produce
        // the same result as one large step of the same total elapsed time.
        var start = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        var totalElapsed = TimeSpan.FromDays(3.5);

        // One big step.
        var bigStep = new EmotionDecayAccumulator(80, 50, start);
        bigStep.AdvanceTo(start + totalElapsed);
        var bigAffinity = bigStep.AffinityFloat;

        // Many small steps (1000 steps of 3.5/1000 days each).
        var smallSteps = new EmotionDecayAccumulator(80, 50, start);
        var stepSize = totalElapsed / 1000;
        for (int i = 0; i < 1000; i++)
        {
            smallSteps.AdvanceTo(start + stepSize * (i + 1));
        }
        var smallAffinity = smallSteps.AffinityFloat;

        // Should match to within floating-point tolerance.
        Assert.AreEqual(bigAffinity, smallAffinity, 0.001,
            "Many small decay steps should match one big step of the same total time.");
    }

    [TestMethod]
    public void Accumulator_ManySmallSteps_MatchOneBigStep_Grievance()
    {
        var start = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        var totalElapsed = TimeSpan.FromHours(18);

        // One big step.
        var bigStep = new EmotionDecayAccumulator(50, 90, start);
        bigStep.AdvanceTo(start + totalElapsed);
        var bigGrievance = bigStep.GrievanceFloat;

        // Many small steps.
        var smallSteps = new EmotionDecayAccumulator(50, 90, start);
        var stepSize = totalElapsed / 1000;
        for (int i = 0; i < 1000; i++)
        {
            smallSteps.AdvanceTo(start + stepSize * (i + 1));
        }
        var smallGrievance = smallSteps.GrievanceFloat;

        Assert.AreEqual(bigGrievance, smallGrievance, 0.001,
            "Many small grievance decay steps should match one big step.");
    }

    [TestMethod]
    public void Accumulator_IntegerRounding_DoesNotLoseDecayOverTime()
    {
        // Simulate per-tick behavior where each tick is very short.
        // With int-only approach, each tick would round to no change.
        // With float accumulator, decay should still progress.
        var start = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        var acc = new EmotionDecayAccumulator(80, 50, start);

        // 1000 ticks of 1 second each (~16.7 minutes total).
        for (int i = 1; i <= 1000; i++)
        {
            acc.AdvanceTo(start + TimeSpan.FromSeconds(i));
        }

        // After ~16.7 minutes, affinity should have decayed slightly.
        // With int-only per-tick rounding, this would still be 80.
        Assert.IsTrue(acc.AffinityFloat < 80.0,
            "Float accumulator should show decay after many small ticks where int-only would not.");
        Assert.IsTrue(acc.AffinityInt == 80 || acc.AffinityInt == 79,
            "After ~17 min of 7-day half-life, int snapshot should be 79 or 80 (very small change).");
    }

    [TestMethod]
    public void Accumulator_ClockRollback_TreatedAsZeroElapsed()
    {
        var start = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        var acc = new EmotionDecayAccumulator(80, 90, start);

        // Clock rolls back.
        acc.AdvanceTo(start - TimeSpan.FromHours(2));

        Assert.AreEqual(80, acc.AffinityInt);
        Assert.AreEqual(90, acc.GrievanceInt);
    }

    [TestMethod]
    public void Accumulator_AddAffinity_PreservesFloatPrecision()
    {
        var start = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        var acc = new EmotionDecayAccumulator(60, 50, start);

        // Decay a tiny bit, then add pet increment.
        acc.AdvanceTo(start + TimeSpan.FromMinutes(1));
        var beforePet = acc.AffinityFloat;
        acc.AddAffinity(5);

        Assert.AreEqual(beforePet + 5.0, acc.AffinityFloat, 0.0001,
            "AddAffinity should add to the current float value, not a rounded int.");
    }

    [TestMethod]
    public void Accumulator_ResetGrievance_SetsToNeutral()
    {
        var start = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        var acc = new EmotionDecayAccumulator(50, 85, start);

        acc.ResetGrievanceAfterExpression();

        Assert.AreEqual(EmotionState.Neutral, acc.GrievanceInt);
        Assert.AreEqual(EmotionState.Neutral, acc.GrievanceFloat, 0.0001);
    }

    [TestMethod]
    public void Accumulator_AdvanceThenSnapshot_MatchesDirectFloatDecay()
    {
        // Verify that AdvanceTo produces the same float as a direct DecayAffinityFloat call.
        var start = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        var elapsed = TimeSpan.FromDays(2.5);

        var direct = EmotionDecay.DecayAffinityFloat(80.0, elapsed.TotalDays);

        var acc = new EmotionDecayAccumulator(80, 50, start);
        acc.AdvanceTo(start + elapsed);

        Assert.AreEqual(direct, acc.AffinityFloat, 0.0001);
    }
}
