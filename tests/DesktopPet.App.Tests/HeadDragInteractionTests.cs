using System.Reflection;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using DesktopPet.Core.Emotion;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Point = System.Windows.Point;

namespace DesktopPet.App.Tests;

// Runs the exact coordinate-processing entry points called by the real mouse
// handlers after CaptureMouse succeeds. No system cursor/input injection here.
[TestClass]
public class HeadDragInteractionTests
{
    private static void Action(MainWindow window, string action)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((AnimationPlayer)typeof(MainWindow).GetField("_animPlayer", flags)!.GetValue(window)!).PlayAction(action);
        typeof(MainWindow).GetMethod("UpdateFrame", flags)!.Invoke(window, null);
    }

    private static Point PressScreen(MainWindow window, Point local) => new(
        window.Left + local.X + window.CurrentFrameOffset.x,
        window.Top + local.Y + window.CurrentFrameOffset.y);

    [STATestMethod]
    [DataRow("idle", 34.0, 18.0)]
    [DataRow("idle", 85.0, 18.0)]
    [DataRow("idle", 60.0, 25.0)]
    [DataRow("curious", 34.0, 18.0)]
    [DataRow("curious", 85.0, 18.0)]
    [DataRow("curious", 60.0, 25.0)]
    public void VisibleEarAndForehead_StartDragOnFirstLargeMove_KeepOriginalAnchor(string action, double x, double y)
    {
        var window = new MainWindow();
        try
        {
            Action(window, action);
            var local = new Point(x, y - window.CurrentFrameOffset.y);
            Assert.IsTrue(window.IsInHeadZone(local), "Test point must hit a real visible head pixel.");
            var press = PressScreen(window, local);
            double left = window.Left, top = window.Top;
            Assert.IsTrue(window.BeginCapturedPointerInteraction(local, press));
            Assert.IsTrue(window.IsPettingCandidate);
            window.ProcessCapturedPointerMove(local + new System.Windows.Vector(25, 12),
                press + new System.Windows.Vector(25, 12), true, 33);
            Assert.IsTrue(window.IsDragging);
            Assert.IsFalse(window.IsPettingCandidate);
            Assert.AreEqual("drag", window.CurrentAction);
            Assert.AreEqual(left + 25, window.Left, 0.001, "First drag event must include the original cursor delta.");
            Assert.AreEqual(top + 12, window.Top, 0.001);
            window.EndCapturedPointerInteraction();
            Assert.IsFalse(window.IsDragging);
            Assert.AreEqual("idle", window.CurrentAction);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void SlowTwoDipSteps_EventuallyDrag_InsteadOfRemainingInPettingForever()
    {
        var window = new MainWindow();
        try
        {
            var local = new Point(60, 25);
            var press = PressScreen(window, local);
            double left = window.Left;
            Assert.IsTrue(window.BeginCapturedPointerInteraction(local, press));
            for (int step = 1; step <= 6; step++)
            {
                var delta = new System.Windows.Vector(step * 2, 0);
                window.ProcessCapturedPointerMove(local + delta, press + delta, true, step * 100);
                Assert.AreEqual(step > 4, window.IsDragging, "8 DIP is slop; cumulative movement above it must drag.");
            }
            Assert.AreEqual(left + 12, window.Left, 0.001);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void CrossingHeadBoundary_WithinSlop_ContinuesAsDrag()
    {
        var window = new MainWindow();
        try
        {
            var local = new Point(60, 39);
            var press = PressScreen(window, local);
            double top = window.Top;
            Assert.IsTrue(window.BeginCapturedPointerInteraction(local, press));
            window.ProcessCapturedPointerMove(new Point(60, 41), press + new System.Windows.Vector(0, 2), true, 33);
            Assert.IsTrue(window.IsDragging, "Leaving the head must not discard the held gesture.");
            Assert.AreEqual(top + 2, window.Top, 0.001);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void MoveOutsideImage_StillDragsAndReleaseStopsFollowing()
    {
        var window = new MainWindow();
        try
        {
            var local = new Point(60, 25);
            var press = PressScreen(window, local);
            double left = window.Left, top = window.Top;
            window.BeginCapturedPointerInteraction(local, press);
            window.ProcessCapturedPointerMove(new Point(230, 180), press + new System.Windows.Vector(170, 155), true, 33);
            Assert.IsTrue(window.IsDragging);
            Assert.AreEqual(left + 170, window.Left, 0.001);
            Assert.AreEqual(top + 155, window.Top, 0.001);
            window.EndCapturedPointerInteraction();
            double stoppedLeft = window.Left, stoppedTop = window.Top;
            window.ProcessCapturedPointerMove(new Point(250, 200), press + new System.Windows.Vector(190, 175), false, 66);
            Assert.AreEqual(stoppedLeft, window.Left);
            Assert.AreEqual(stoppedTop, window.Top);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void HeadMicroStrokes_StillAwardPetting_WithoutDragging()
    {
        var window = new MainWindow();
        try
        {
            double now = 0;
            window.TestGetElapsedMs = () => now;
            var emotion = new EmotionDecayAccumulator(50, 50, DateTime.UtcNow);
            window.TestEmotionAccumulator = emotion;
            var local = new Point(60, 25);
            var press = PressScreen(window, local);
            double left = window.Left, top = window.Top;
            window.BeginCapturedPointerInteraction(local, press);
            for (int i = 1; i <= 12; i++)
            {
                now = i * 100;
                var delta = new System.Windows.Vector(i % 2 == 0 ? 0 : 2, 0);
                window.ProcessCapturedPointerMove(local + delta, press + delta, true, now);
            }
            Assert.IsTrue(window.IsPettingCandidate);
            Assert.IsFalse(window.IsDragging);
            window.EndCapturedPointerInteraction();
            Assert.AreEqual(55, emotion.AffinityInt);
            Assert.AreEqual(left, window.Left);
            Assert.AreEqual(top, window.Top);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PettingConvertedToDrag_DoesNotAwardAccumulatedPetting()
    {
        var window = new MainWindow();
        try
        {
            double now = 0;
            window.TestGetElapsedMs = () => now;
            var emotion = new EmotionDecayAccumulator(50, 50, DateTime.UtcNow);
            window.TestEmotionAccumulator = emotion;
            var local = new Point(60, 25);
            var press = PressScreen(window, local);
            window.BeginCapturedPointerInteraction(local, press);
            for (int i = 1; i <= 12; i++)
            {
                now = i * 100;
                var delta = new System.Windows.Vector(i % 2 == 0 ? 0 : 2, 0);
                window.ProcessCapturedPointerMove(local + delta, press + delta, true, now);
            }
            now = 1300;
            window.ProcessCapturedPointerMove(new Point(80, 25), press + new System.Windows.Vector(20, 0), true, now);
            window.EndCapturedPointerInteraction();
            Assert.AreEqual(50, emotion.AffinityInt, "A drag must not finish/score the earlier petting candidate.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [DataRow(4.0, 4.0)]
    [DataRow(60.0, 5.0)]
    public void TransparentHeadSpace_DoesNotBeginAnyInteraction(double x, double y)
    {
        var window = new MainWindow();
        try
        {
            Assert.IsFalse(window.BeginCapturedPointerInteraction(new Point(x, y), new Point(100 + x, 100 + y)));
            Assert.IsFalse(window.IsDragging);
            Assert.IsFalse(window.IsPettingCandidate);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void HeadClickWithoutMovement_IsHandled_WithoutMovingOrScoring()
    {
        var window = new MainWindow();
        try
        {
            var emotion = new EmotionDecayAccumulator(50, 50, DateTime.UtcNow);
            window.TestEmotionAccumulator = emotion;
            var local = new Point(60, 25);
            double left = window.Left, top = window.Top;
            Assert.IsTrue(window.BeginCapturedPointerInteraction(local, PressScreen(window, local)));
            window.EndCapturedPointerInteraction();
            Assert.IsFalse(window.IsPettingCandidate);
            Assert.AreEqual(50, emotion.AffinityInt);
            Assert.AreEqual(left, window.Left);
            Assert.AreEqual(top, window.Top);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void HeadToDrag_PreservesRealWpfCapture_UntilRelease()
    {
        var window = new MainWindow();
        try
        {
            // Exercise actual WPF capture on our own window; no cursor movement
            // or button injection, and no user archive services are created.
            window.Show();
            var canvas = (System.Windows.Controls.Canvas)window.FindName("PetCanvas");
            Assert.IsTrue(canvas.CaptureMouse());
            Assert.AreSame(canvas, System.Windows.Input.Mouse.Captured);
            var local = new Point(60, 25);
            var press = PressScreen(window, local);
            Assert.IsTrue(window.BeginCapturedPointerInteraction(local, press));
            window.ProcessCapturedPointerMove(new Point(85, 25), press + new System.Windows.Vector(25, 0), true, 33);
            Assert.IsTrue(window.IsDragging);
            Assert.AreSame(canvas, System.Windows.Input.Mouse.Captured,
                "Head-to-drag conversion must retain capture even when leaving the initial head region.");
            window.EndCapturedPointerInteraction();
            Assert.AreNotSame(canvas, System.Windows.Input.Mouse.Captured);
            Assert.IsFalse(window.IsDragging);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void BodyPress_StillStartsDragImmediately()
    {
        var window = new MainWindow();
        try
        {
            var local = new Point(60, 80);
            var press = PressScreen(window, local);
            double left = window.Left, top = window.Top;
            Assert.IsTrue(window.BeginCapturedPointerInteraction(local, press));
            Assert.IsTrue(window.IsDragging);
            Assert.IsFalse(window.IsPettingCandidate);
            window.ProcessCapturedPointerMove(new Point(62, 82), press + new System.Windows.Vector(2, 2), true, 33);
            Assert.AreEqual(left + 2, window.Left, 0.001);
            Assert.AreEqual(top + 2, window.Top, 0.001);
        }
        finally { window.Close(); }
    }
}
