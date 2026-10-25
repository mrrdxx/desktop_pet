using DesktopPet.Core.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

[TestClass]
public class QuietExpressionTests
{
    private static ActionDef Blink => new() { Id = "blink", Loop = LoopMode.Once, Frames = [
        new() { ImagePath = "half", DurationMs = 40 },
        new() { ImagePath = "closed", DurationMs = 70 },
        new() { ImagePath = "half", DurationMs = 70 },
        new() { ImagePath = "open", DurationMs = 40 }
    ] };
    private static void Advance(IdleBlinkScheduler scheduler, double end)
    {
        for (double t = 100; t < end; t += 100) scheduler.Update(t, true);
    }

    [TestMethod]
    [DataRow(0.0, 3000.0)]
    [DataRow(0.5, 5000.0)]
    [DataRow(1.0, 7000.0)]
    public void Blink_QuietIntervalUsesRandomRange(double random, double expected)
    {
        var s = new IdleBlinkScheduler(Blink, () => random);
        Assert.IsNull(s.Update(0, true));
        Assert.AreEqual(expected, s.NextBlinkAtMs);
        Advance(s, expected);
        Assert.AreEqual("half", s.Update(expected, true)!.ImagePath);
    }

    [TestMethod]
    public void Blink_OrderedAsymmetricPhases_ThenFreshInterval()
    {
        var s = new IdleBlinkScheduler(Blink, () => 0.5);
        s.Update(0, true); Advance(s, 5000);
        Assert.AreEqual("half", s.Update(5000, true)!.ImagePath);
        Assert.AreEqual("half", s.Update(5039, true)!.ImagePath);
        Assert.AreEqual("closed", s.Update(5040, true)!.ImagePath);
        Assert.AreEqual("closed", s.Update(5109, true)!.ImagePath);
        Assert.AreEqual("half", s.Update(5110, true)!.ImagePath);
        Assert.AreEqual("open", s.Update(5180, true)!.ImagePath);
        Assert.IsNull(s.Update(5220, true));
        Assert.IsFalse(s.IsBlinking);
        Assert.AreEqual(10220, s.NextBlinkAtMs);
    }

    [TestMethod]
    public void Blink_RareDoubleHasOpenGap_AndNeverThirdBlink()
    {
        var s = new IdleBlinkScheduler(Blink, () => 0.0);
        s.Update(0, true); Advance(s, 3000);
        Assert.AreEqual("half", s.Update(3000, true)!.ImagePath);
        Assert.IsNull(s.Update(3220, true));
        Assert.IsNull(s.Update(3359, true));
        Assert.AreEqual("half", s.Update(3360, true)!.ImagePath);
        Assert.AreEqual("closed", s.Update(3400, true)!.ImagePath);
        Assert.IsNull(s.Update(3580, true));
        Assert.AreEqual(6580, s.NextBlinkAtMs);
    }

    [TestMethod]
    public void Blink_InterruptionCancelsPending_ThenDefersNewBlink()
    {
        var s = new IdleBlinkScheduler(Blink, () => 0.5);
        s.Update(0, true); Advance(s, 5000); s.Update(5000, true);
        Assert.IsNull(s.Update(5010, false));
        Assert.IsFalse(s.IsBlinking);
        Assert.IsNull(s.Update(90000, false));
        Assert.IsNull(s.Update(90010, true));
        Assert.AreEqual(95010, s.NextBlinkAtMs);
    }

    [TestMethod]
    public void Blink_PauseFreezesCurrentPhase_ResumeDoesNotCatchUp()
    {
        var s = new IdleBlinkScheduler(Blink, () => 0.5);
        s.Update(0, true); Advance(s, 5000); s.Update(5000, true);
        s.Pause(5020);
        Assert.AreEqual("half", s.Update(90000, true)!.ImagePath);
        s.Resume(90000);
        Assert.AreEqual("half", s.Update(90019, true)!.ImagePath);
        Assert.AreEqual("closed", s.Update(90020, true)!.ImagePath);
    }

    [TestMethod]
    public void Blink_LongUpdateGapDiscardsOverdueWork()
    {
        var s = new IdleBlinkScheduler(Blink, () => 0.5);
        s.Update(0, true);
        Assert.IsNull(s.Update(60000, true));
        Assert.AreEqual(65000, s.NextBlinkAtMs);
        Assert.IsFalse(s.IsBlinking);
    }

    [TestMethod]
    public void Curiosity_EndsAtTwoSeconds_WithoutMouseLeave()
    {
        var c = new CuriosityReaction(); c.Start(1200);
        Assert.IsFalse(c.ShouldEnd(3199, true));
        Assert.IsTrue(c.ShouldEnd(3200, true));
    }

    [TestMethod]
    public void Curiosity_ContinuousOutsideGrace_Is300ms()
    {
        var c = new CuriosityReaction(); c.Start(0);
        Assert.IsFalse(c.ShouldEnd(200, false));
        for (int t = 230; t < 500; t += 30) Assert.IsFalse(c.ShouldEnd(t, false));
        Assert.IsTrue(c.ShouldEnd(500, false));
    }

    [TestMethod]
    public void Curiosity_ShortOutsideReturn_CancelsDeparture()
    {
        var c = new CuriosityReaction(); c.Start(0);
        c.ShouldEnd(100, false);
        Assert.IsFalse(c.ShouldEnd(399, true));
        Assert.IsFalse(c.ShouldEnd(600, false));
        Assert.IsFalse(c.ShouldEnd(899, false));
        Assert.IsTrue(c.ShouldEnd(900, false));
    }

    [TestMethod]
    public void Curiosity_PauseSkipsLifetime_AndStartsFreshDeparture()
    {
        var c = new CuriosityReaction(); c.Start(0);
        c.ShouldEnd(100, false); c.Pause(200);
        Assert.IsFalse(c.ShouldEnd(90000, false));
        c.Resume(90000);
        Assert.IsFalse(c.ShouldEnd(90000, false));
        Assert.IsFalse(c.ShouldEnd(90299, false));
        Assert.IsTrue(c.ShouldEnd(90300, false));
        Assert.IsFalse(c.ShouldEnd(91799, true));
        Assert.IsTrue(c.ShouldEnd(91800, true));
    }

    [TestMethod]
    public void Curiosity_CancelRemovesOldDeadline()
    {
        var c = new CuriosityReaction(); c.Start(0); c.Cancel();
        Assert.IsFalse(c.IsActive);
        Assert.IsFalse(c.ShouldEnd(99999, false));
        c.Start(100000);
        Assert.IsFalse(c.ShouldEnd(101999, true));
        Assert.IsTrue(c.ShouldEnd(102000, true));
    }
}
