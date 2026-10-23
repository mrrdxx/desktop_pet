using DesktopPet.Core.Emotion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// EmotionState boundary, increment, and threshold tests.
/// </summary>
[TestClass]
public class EmotionStateTests
{
    // ── Boundary clamping ──────────────────────────────────────────────

    [TestMethod]
    public void Affinity_DefaultsToNeutral()
    {
        var state = new EmotionState();
        Assert.AreEqual(EmotionState.Neutral, state.Affinity);
        Assert.AreEqual(EmotionState.Neutral, state.Grievance);
    }

    [TestMethod]
    public void Affinity_ClampsToMax()
    {
        var state = new EmotionState { Affinity = 200 };
        Assert.AreEqual(EmotionState.MaxValue, state.Affinity);
    }

    [TestMethod]
    public void Affinity_ClampsToMin()
    {
        var state = new EmotionState { Affinity = -50 };
        Assert.AreEqual(EmotionState.MinValue, state.Affinity);
    }

    [TestMethod]
    public void Grievance_ClampsToMax()
    {
        var state = new EmotionState { Grievance = 150 };
        Assert.AreEqual(EmotionState.MaxValue, state.Grievance);
    }

    [TestMethod]
    public void Grievance_ClampsToMin()
    {
        var state = new EmotionState { Grievance = -10 };
        Assert.AreEqual(EmotionState.MinValue, state.Grievance);
    }

    // ── Pet increment ──────────────────────────────────────────────────

    [TestMethod]
    public void AddPetIncrement_DefaultAdds5()
    {
        var state = new EmotionState();
        var result = state.AddPetIncrement();
        Assert.AreEqual(55, result);
        Assert.AreEqual(55, state.Affinity);
    }

    [TestMethod]
    public void AddPetIncrement_CustomAmount()
    {
        var state = new EmotionState();
        state.AddPetIncrement(10);
        Assert.AreEqual(60, state.Affinity);
    }

    [TestMethod]
    public void AddPetIncrement_ClampsAtMax()
    {
        var state = new EmotionState { Affinity = 98 };
        state.AddPetIncrement(10);
        Assert.AreEqual(EmotionState.MaxValue, state.Affinity);
    }

    [TestMethod]
    public void AddPetIncrement_MultipleCallsAccumulate()
    {
        var state = new EmotionState();
        state.AddPetIncrement(); // 55
        state.AddPetIncrement(); // 60
        state.AddPetIncrement(); // 65
        Assert.AreEqual(65, state.Affinity);
    }

    // ── Corner grievance ───────────────────────────────────────────────

    [TestMethod]
    public void AddCornerGrievance_DefaultAdds20()
    {
        var state = new EmotionState();
        var result = state.AddCornerGrievance();
        Assert.AreEqual(70, result);
        Assert.AreEqual(70, state.Grievance);
    }

    [TestMethod]
    public void AddCornerGrievance_ClampsAtMax()
    {
        var state = new EmotionState { Grievance = 85 };
        state.AddCornerGrievance();
        Assert.AreEqual(EmotionState.MaxValue, state.Grievance);
    }

    [TestMethod]
    public void AddCornerGrievance_DoesNotAccumulateRepeatedly_WhenCallerControls()
    {
        // The caller is responsible for only calling AddCornerGrievance after
        // CornerTracker.IsConditionMet returns true and then clearing the tracker.
        // EmotionState itself just applies the increment; verify it doesn't
        // have hidden state that prevents a second legitimate award.
        var state = new EmotionState();
        state.AddCornerGrievance();  // 70
        state.AddCornerGrievance();  // 90 — second legitimate corner condition
        Assert.AreEqual(90, state.Grievance);
    }

    // ── Reset after expression ─────────────────────────────────────────

    [TestMethod]
    public void ResetGrievanceAfterExpression_SetsToNeutral()
    {
        var state = new EmotionState { Grievance = 85 };
        state.ResetGrievanceAfterExpression();
        Assert.AreEqual(EmotionState.Neutral, state.Grievance);
    }

    [TestMethod]
    public void ResetGrievanceAfterExpression_DoesNotAffectAffinity()
    {
        var state = new EmotionState { Affinity = 75, Grievance = 90 };
        state.ResetGrievanceAfterExpression();
        Assert.AreEqual(75, state.Affinity);
        Assert.AreEqual(EmotionState.Neutral, state.Grievance);
    }

    // ── Threshold checks ───────────────────────────────────────────────

    [TestMethod]
    public void IsHighAffinity_TrueAtThreshold()
    {
        var state = new EmotionState { Affinity = EmotionState.HighAffinityThreshold };
        Assert.IsTrue(state.IsHighAffinity);
    }

    [TestMethod]
    public void IsHighAffinity_FalseBelowThreshold()
    {
        var state = new EmotionState { Affinity = EmotionState.HighAffinityThreshold - 1 };
        Assert.IsFalse(state.IsHighAffinity);
    }

    [TestMethod]
    public void IsGrievanceThresholdReached_TrueAtThreshold()
    {
        var state = new EmotionState { Grievance = EmotionState.GrievanceExpressionThreshold };
        Assert.IsTrue(state.IsGrievanceThresholdReached);
    }

    [TestMethod]
    public void IsGrievanceThresholdReached_FalseBelowThreshold()
    {
        var state = new EmotionState { Grievance = EmotionState.GrievanceExpressionThreshold - 1 };
        Assert.IsFalse(state.IsGrievanceThresholdReached);
    }

    // ── Neutral value constant ─────────────────────────────────────────

    [TestMethod]
    public void Neutral_Is50()
    {
        Assert.AreEqual(50, EmotionState.Neutral);
    }
}
