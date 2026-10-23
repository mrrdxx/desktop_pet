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
}
