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

    // ── Threshold checks ───────────────────────────────────────────────

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
