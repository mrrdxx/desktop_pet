using System.Reflection;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

// Drives the real tick with controlled cursor/time. D-009 removes velocity
// reactions; remaining dwell and interruption contracts must still hold.
[TestClass]
public class HoverInteractionIntegrationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Tick(MainWindow window) => typeof(MainWindow)
        .GetMethod("OnUpdateTick", Private)!.Invoke(window, [null, EventArgs.Empty]);
    private static AnimationPlayer Player(MainWindow window) => (AnimationPlayer)typeof(MainWindow)
        .GetField("_animPlayer", Private)!.GetValue(window)!;
    private static void Set(MainWindow window, string field, object value) => typeof(MainWindow)
        .GetField(field, Private)!.SetValue(window, value);

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LeavingCuriosity_ReturnsIdleWithoutClick_After300ms(bool transparentPoint)
    {
        var window = new MainWindow();
        try
        {
            double now = 0;
            window.TestGetElapsedMs = () => now;
            window.TestGetCursorPos = () => (160, 165);
            Tick(window);
            now = 1200; Tick(window);
            Assert.AreEqual("curious", window.CurrentAction);
            window.TestGetCursorPos = transparentPoint ? () => (104, 104) : () => (900, 900);
            now = 1300; Tick(window);
            now = 1599; Tick(window);
            Assert.AreEqual("curious", window.CurrentAction, "Brief boundary crossings keep the face stable.");
            now = 1600; Tick(window);
            Assert.AreEqual("idle", window.CurrentAction, "Leaving alone must restore idle, with no click or drag.");
            Assert.AreEqual(0.0, window.CurrentFrameOffset.y, "The curious rendering offset must be removed too.");
            now = 4000; Tick(window);
            Assert.AreEqual("idle", window.CurrentAction);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void BriefLeave_ReenterDoesNotFlickerOrReplayCuriosity()
    {
        var window = new MainWindow();
        try
        {
            double now = 0;
            window.TestGetElapsedMs = () => now;
            window.TestGetCursorPos = () => (160, 165);
            Tick(window); now = 1200; Tick(window);
            var player = Player(window);
            var origin = typeof(AnimationPlayer).GetField("_actionStartMs", Private)!.GetValue(player);
            window.TestGetCursorPos = () => (900, 900);
            now = 1300; Tick(window);
            window.TestGetCursorPos = () => (160, 165);
            now = 1500; Tick(window); now = 1900; Tick(window);
            Assert.AreEqual("curious", window.CurrentAction);
            Assert.AreEqual(origin, typeof(AnimationPlayer).GetField("_actionStartMs", Private)!.GetValue(player));
            now = 3200; Tick(window);
            Assert.AreEqual("idle", window.CurrentAction, "D-011 ends curiosity even under continuous hover.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void LeaveThenReturn_RequiresNewFullDwell()
    {
        var window = new MainWindow();
        try
        {
            double now = 0;
            window.TestGetElapsedMs = () => now;
            window.TestGetCursorPos = () => (160, 165);
            Tick(window); now = 1200; Tick(window);
            window.TestGetCursorPos = () => (900, 900);
            now = 1300; Tick(window); now = 1800; Tick(window);
            Assert.AreEqual("idle", window.CurrentAction);
            window.TestGetCursorPos = () => (160, 165);
            now = 2000; Tick(window); now = 3199; Tick(window);
            Assert.AreEqual("idle", window.CurrentAction);
            now = 3200; Tick(window);
            Assert.AreEqual("curious", window.CurrentAction);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [DataRow("drag")]
    [DataRow("grievance")]
    [DataRow("happy")]
    public void LeaveEvent_DoesNotOverwriteOtherActions(string action)
    {
        var window = new MainWindow();
        try
        {
            double now = 0;
            window.TestGetElapsedMs = () => now;
            window.TestGetCursorPos = () => (160, 165);
            Tick(window); now = 1200; Tick(window);
            Player(window).PlayAction(action);
            window.TestGetCursorPos = () => (900, 900);
            now = 1300; Tick(window); now = 1800; Tick(window);
            Assert.AreEqual(action, window.CurrentAction);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [DataRow("pause")]
    [DataRow("menu")]
    [DataRow("petting")]
    public void LeaveAfterInterrupt_StillEndsCuriosity_WithFreshGracePeriod(string kind)
    {
        var window = new MainWindow();
        try
        {
            double now = 0;
            window.TestGetElapsedMs = () => now;
            window.TestGetCursorPos = () => (160, 165);
            Tick(window); now = 1200; Tick(window);
            if (kind == "pause") window.PauseAnimation();
            else Set(window, kind == "menu" ? "_isContextMenuOpen" : "_isPettingCandidate", true);
            window.TestGetCursorPos = () => (900, 900);
            now = 5000; Tick(window);
            Assert.AreEqual("curious", window.CurrentAction, "Interrupt tick must not overwrite frozen/owned animation.");
            if (kind == "pause") window.ResumeAnimation();
            else Set(window, kind == "menu" ? "_isContextMenuOpen" : "_isPettingCandidate", false);
            now = 5100; Tick(window); now = 5399; Tick(window);
            Assert.AreEqual("curious", window.CurrentAction);
            now = 5400; Tick(window);
            Assert.AreEqual("idle", window.CurrentAction, "Reset detector must not orphan the curious face.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Dwell_TriggersOnce_LeaveResets_AndNewDwellIsRequired()
    {
        var window = new MainWindow();
        try
        {
            double now = 0;
            window.TestGetElapsedMs = () => now;
            window.TestGetCursorPos = () => (160, 165);
            Tick(window);
            now = 1199; Tick(window);
            Assert.AreEqual("idle", window.CurrentAction);
            now = 1200; Tick(window);
            Assert.AreEqual("curious", window.CurrentAction);
            Player(window).PlayAction("idle");
            now = 1600; Tick(window);
            Assert.AreEqual("idle", window.CurrentAction, "Continued dwell must not replay curious.");
            window.TestGetCursorPos = () => (900, 900);
            for (now = 1700; now <= 2300; now += 100) Tick(window);
            window.TestGetCursorPos = () => (160, 165);
            now = 2400; Tick(window);
            now = 3599; Tick(window);
            Assert.AreEqual("idle", window.CurrentAction);
            now = 3600; Tick(window);
            Assert.AreEqual("curious", window.CurrentAction, "A new dwell after leaving must trigger again.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [DataRow(1.0)]
    [DataRow(33.0)]
    [DataRow(100.0)]
    public void RapidMouseMovement_ThroughAndNearBody_DoesNotChangeIdle(double stepMs)
    {
        var window = new MainWindow();
        try
        {
            double now = 0;
            window.TestGetElapsedMs = () => now;
            // Alternating far/near positions exceeds the old velocity threshold
            // but never accumulates dwell. Verify behavior, not field absence.
            foreach (var position in new[] { (800, 165), (170, 165), (900, 165), (150, 165), (160, 700) })
            {
                window.TestGetCursorPos = () => position;
                Tick(window);
                Assert.AreEqual("idle", window.CurrentAction,
                    "Fast cursor movement alone must not change expression after D-009.");
                now += stepMs;
            }
            Assert.IsFalse(window.HoverDetector.HasTriggered);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    [DataRow("drag")]
    [DataRow("menu")]
    [DataRow("pause")]
    [DataRow("petting")]
    public void Interruption_ResetsExistingHover_AndResumeRequiresFullFreshDwell(string kind)
    {
        var window = new MainWindow();
        try
        {
            double now = 0;
            window.TestGetElapsedMs = () => now;
            window.TestGetCursorPos = () => (160, 165);
            Tick(window);
            now = 1200; Tick(window);
            Assert.IsTrue(window.HoverDetector.HasTriggered, "Build real hover state before interrupting.");
            Player(window).PlayAction("idle");
            if (kind == "pause") window.PauseAnimation();
            else Set(window, kind switch {
                "drag" => "_isDragging", "menu" => "_isContextMenuOpen", _ => "_isPettingCandidate"
            }, true);
            now = 2500; Tick(window);
            Assert.IsFalse(window.HoverDetector.IsHovering);
            Assert.IsFalse(window.HoverDetector.HasTriggered);
            Assert.AreEqual("idle", window.CurrentAction);
            if (kind == "pause") window.ResumeAnimation();
            else Set(window, kind switch {
                "drag" => "_isDragging", "menu" => "_isContextMenuOpen", _ => "_isPettingCandidate"
            }, false);
            now = 3000; Tick(window);
            now = 4199; Tick(window);
            Assert.AreEqual("idle", window.CurrentAction, "Pre-interrupt time must not count toward new dwell.");
            now = 4200; Tick(window);
            Assert.AreEqual("curious", window.CurrentAction);
        }
        finally { window.Close(); }
    }
}
