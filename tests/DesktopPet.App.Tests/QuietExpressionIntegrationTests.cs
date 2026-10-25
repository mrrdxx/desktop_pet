using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

[TestClass]
public class QuietExpressionIntegrationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Tick(MainWindow w) => typeof(MainWindow).GetMethod("OnUpdateTick", Private)!.Invoke(w, [null, EventArgs.Empty]);
    private static IdleBlinkScheduler Scheduler(MainWindow w) => (IdleBlinkScheduler)typeof(MainWindow).GetField("_idleBlink", Private)!.GetValue(w)!;
    private static string ImagePath(MainWindow w) => ((BitmapImage)((Image)w.FindName("PetImage")).Source).UriSource.LocalPath;

    [STATestMethod]
    public void ContinuousHover_EndsCuriosity_AndDoesNotRepeat()
    {
        var w = new MainWindow(); double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now; w.TestGetCursorPos = () => (160,165);
            Tick(w); now=1200; Tick(w); Assert.AreEqual("curious", w.CurrentAction);
            now=3199; Tick(w); Assert.AreEqual("curious", w.CurrentAction);
            now=3200; Tick(w); Assert.AreEqual("idle", w.CurrentAction);
            Assert.AreEqual(0.0, w.CurrentFrameOffset.y);
            for (now=3300;now<12000;now+=100) Tick(w);
            Assert.AreEqual("idle", w.CurrentAction);
            Assert.IsTrue(w.HoverDetector.HasTriggered, "Lifetime expiry must not re-arm hover.");
        }
        finally { w.Close(); }
    }

    [STATestMethod]
    public void EarlyLeaveEndsFaceAt300ms_ButRearmStillRequires500ms()
    {
        var w = new MainWindow(); double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now; w.TestGetCursorPos = () => (160,165);
            Tick(w); now=1200; Tick(w);
            w.TestGetCursorPos = () => (900,900); now=1300; Tick(w);
            now=1600; Tick(w); Assert.AreEqual("idle", w.CurrentAction);
            w.TestGetCursorPos = () => (160,165); now=1700; Tick(w);
            now=3000; Tick(w); Assert.AreEqual("idle", w.CurrentAction, "A 400ms leave must not re-arm.");
            w.TestGetCursorPos = () => (900,900); now=3100; Tick(w); now=3600; Tick(w);
            w.TestGetCursorPos = () => (160,165); now=3700; Tick(w);
            now=4899; Tick(w); Assert.AreEqual("idle", w.CurrentAction);
            now=4900; Tick(w); Assert.AreEqual("curious", w.CurrentAction);
        }
        finally { w.Close(); }
    }

    [STATestMethod]
    public void ActualRender_UsesEyeFrames_KeepingIdleIdentity_AndFreezeOnPause()
    {
        var w = new MainWindow(); double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now; w.TestGetCursorPos = () => (900,900);
            Scheduler(w).Update(0,false); Tick(w);
            double next=Scheduler(w).NextBlinkAtMs;
            for(now=100;now<next;now+=100) Tick(w);
            now=next; Tick(w);
            Assert.AreEqual("idle",w.CurrentAction);
            StringAssert.EndsWith(ImagePath(w),"blink_half.png");
            now=next+40; Tick(w); StringAssert.EndsWith(ImagePath(w),"blink_closed.png");
            w.PauseAnimation(); now+=100000; Tick(w);
            StringAssert.EndsWith(ImagePath(w),"blink_closed.png");
            w.ResumeAnimation(); Tick(w);
            StringAssert.EndsWith(ImagePath(w),"blink_closed.png");
            now+=70; Tick(w); StringAssert.EndsWith(ImagePath(w),"blink_half.png");
            Assert.AreEqual(0.0,w.CurrentFrameOffset.y);
        }
        finally { w.Close(); }
    }

    [STATestMethod]
    public void PauseCuriosity_PreservesOnlyRemainingActiveLifetime()
    {
        var w = new MainWindow(); double now = 0;
        try
        {
            w.TestGetElapsedMs=()=>now; w.TestGetCursorPos=()=>(160,165);
            Tick(w); now=1200; Tick(w); now=2000; w.PauseAnimation();
            now=100000; w.ResumeAnimation(); Tick(w);
            Assert.AreEqual("curious",w.CurrentAction);
            now=101199; Tick(w); Assert.AreEqual("curious",w.CurrentAction);
            now=101200; Tick(w); Assert.AreEqual("idle",w.CurrentAction);
        }
        finally { w.Close(); }
    }
}
