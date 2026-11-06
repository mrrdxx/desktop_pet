using DesktopPet.Core.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

[TestClass]
public class DragReleaseFeedbackTests
{
    private static DragReleaseFeedback Create() => new(new ActionDef { Id="release", Frames=[
        new(){ImagePath="gentle",DurationMs=90},
        new(){ImagePath="settle",DurationMs=90},
        new(){ImagePath="idle",DurationMs=70}
    ]});

    [TestMethod]
    public void Release_ShowsOrderedFrames_ThenExpiresAt250ms()
    {
        var f=Create(); f.Start(1000);
        Assert.AreEqual("gentle",f.Update(1000,true)!.ImagePath);
        Assert.AreEqual("gentle",f.Update(1089,true)!.ImagePath);
        Assert.AreEqual("settle",f.Update(1090,true)!.ImagePath);
        Assert.AreEqual("settle",f.Update(1179,true)!.ImagePath);
        Assert.AreEqual("idle",f.Update(1180,true)!.ImagePath);
        Assert.AreEqual("idle",f.Update(1249,true)!.ImagePath);
        Assert.IsNull(f.Update(1250,true)); Assert.IsFalse(f.IsActive);
    }

    [TestMethod]
    public void Interruption_Cancels_AndCannotResumeStaleFeedback()
    {
        var f=Create(); f.Start(0);
        Assert.IsNull(f.Update(20,false));
        Assert.IsFalse(f.IsActive);
        Assert.IsNull(f.Update(30,true));
    }

    [TestMethod]
    public void Pause_FreezesCurrentProgress_ResumeUsesRemainingTime()
    {
        var f=Create(); f.Start(0); f.Pause(100);
        Assert.AreEqual("settle",f.Update(90000,true)!.ImagePath);
        f.Resume(90000);
        Assert.AreEqual("settle",f.Update(90079,true)!.ImagePath);
        Assert.AreEqual("idle",f.Update(90080,true)!.ImagePath);
        Assert.IsNull(f.Update(90150,true));
    }

    [TestMethod]
    public void StartWhilePaused_NewFeedbackFreezesAtItsOwnOrigin()
    {
        var f=Create(); f.Pause(100); f.Start(500);
        Assert.AreEqual("gentle",f.Update(90000,true)!.ImagePath);
        f.Resume(90000);
        Assert.AreEqual("gentle",f.Update(90089,true)!.ImagePath);
        Assert.AreEqual("settle",f.Update(90090,true)!.ImagePath);
        Assert.IsNull(f.Update(90250,true));
    }

    [TestMethod]
    public void NewRelease_RestartsFromFirstFrame()
    {
        var f=Create(); f.Start(0); f.Update(100,true); f.Start(120);
        Assert.AreEqual("gentle",f.Update(120,true)!.ImagePath);
        Assert.AreEqual("settle",f.Update(210,true)!.ImagePath);
    }

    [TestMethod]
    public void LongGap_ExpiresWithoutReplay()
    {
        var f=Create(); f.Start(0);
        Assert.IsNull(f.Update(90000,true));
        Assert.IsNull(f.Update(90010,true));
    }
}
