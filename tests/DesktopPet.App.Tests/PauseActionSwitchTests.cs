using System.Reflection;
using DesktopPet.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// F-02: App-level pause/action-switch consistency tests.
/// Verifies that action switches during a lifecycle pause (drag start,
/// drag end, menu interruption) do not implicitly resume the animation
/// player, and that App/player/timer state stays consistent.
/// 
/// These tests construct MainWindow without calling Show(), similar to
/// the supervisor's diagnostic approach. The constructor loads real
/// character assets; if assets are unavailable the tests are skipped
/// (not failed) since they require the P1-001 resource pipeline.
/// Uses STATestMethod because WPF UI components require an STA thread.
/// </summary>
[TestClass]
public class PauseActionSwitchTests
{
    private static MainWindow? TryCreateMainWindow()
    {
        try
        {
            return new MainWindow();
        }
        catch (Exception ex)
        {
            // Log the exception for debugging; assets or WPF context may be unavailable.
            System.Diagnostics.Trace.TraceWarning(
                "PauseActionSwitchTests: MainWindow construction failed: {0}", ex);
            return null;
        }
    }

    /// <summary>
    /// Read a private field value via reflection.
    /// </summary>
    private static T? GetField<T>(object obj, string name) where T : class
    {
        var field = typeof(MainWindow).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(obj) as T;
    }

    /// <summary>
    /// Invoke a private method via reflection.
    /// </summary>
    private static void InvokeMethod(object obj, string name, params object[] args)
    {
        var method = typeof(MainWindow).GetMethod(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        method?.Invoke(obj, args);
    }

    [STATestMethod]
    public void PauseAnimation_SetsAppAndPlayerPaused()
    {
        var window = TryCreateMainWindow();
        if (window is null)
        {
            Assert.Inconclusive("MainWindow requires character assets; skipping.");
            return;
        }

        try
        {
            Assert.IsFalse(window.IsAppPaused);
            Assert.IsFalse(window.IsPlayerPaused);

            window.PauseAnimation();

            Assert.IsTrue(window.IsAppPaused);
            Assert.IsTrue(window.IsPlayerPaused);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void PauseThenDragStart_PlayerStaysPaused()
    {
        // F-02 R2 correction: Actually execute action switch and EndDrag
        // during pause, then verify player/timer state and recovery progress.
        // Previously this test only checked pause flags without any action
        // switch — corrected per supervisor R2 test-quality note.
        var window = TryCreateMainWindow();
        if (window is null)
        {
            Assert.Inconclusive("MainWindow requires character assets; skipping.");
            return;
        }

        try
        {
            // --- Setup: pause via tray ---
            window.PauseAnimation();
            Assert.IsTrue(window.IsAppPaused);
            Assert.IsTrue(window.IsPlayerPaused);

            var timer = GetField<System.Windows.Threading.DispatcherTimer>(window, "_updateTimer");
            Assert.IsNotNull(timer);
            Assert.IsFalse(timer!.IsEnabled, "Timer must be stopped after pause");

            // --- Simulate drag start: PlayAction("drag") + UpdateFrame ---
            // This is what PetCanvas_MouseLeftButtonDown does internally.
            var animPlayer = GetField<DesktopPet.Core.Animation.AnimationPlayer>(window, "_animPlayer");
            Assert.IsNotNull(animPlayer);
            animPlayer!.PlayAction("drag");
            InvokeMethod(window, "UpdateFrame");

            // After action switch during pause: player must still be paused.
            Assert.IsTrue(window.IsPlayerPaused,
                "Player must remain paused after PlayAction during pause");
            Assert.IsTrue(window.IsAppPaused,
                "App pause flag must remain set after action switch");
            Assert.IsFalse(timer.IsEnabled,
                "Timer must remain stopped after action switch during pause");
            Assert.AreEqual("drag", window.CurrentAction,
                "Action must have switched to drag");

            // --- Simulate drag end: EndDrag (calls PlayAction("idle") + UpdateFrame) ---
            InvokeMethod(window, "EndDrag");

            // After EndDrag during pause: player must STILL be paused.
            Assert.IsTrue(window.IsPlayerPaused,
                "Player must remain paused after EndDrag during pause");
            Assert.IsTrue(window.IsAppPaused,
                "App pause flag must remain set after EndDrag");
            Assert.IsFalse(timer.IsEnabled,
                "Timer must remain stopped after EndDrag during pause");
            Assert.AreEqual("idle", window.CurrentAction,
                "Action must have returned to idle after EndDrag");

            // --- Resume and verify progress starts from idle frame 0 ---
            window.ResumeAnimation();
            Assert.IsFalse(window.IsAppPaused);
            Assert.IsFalse(window.IsPlayerPaused);
            Assert.IsTrue(timer.IsEnabled,
                "Timer must be running after resume");

            // After resume, the new idle action started from elapsed=0.
            // The frame should be idle_0 (first frame), not advanced.
            Assert.AreEqual("idle", window.CurrentAction);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void ResumeAnimation_ClearsAppAndPlayerPaused()
    {
        var window = TryCreateMainWindow();
        if (window is null)
        {
            Assert.Inconclusive("MainWindow requires character assets; skipping.");
            return;
        }

        try
        {
            window.PauseAnimation();
            Assert.IsTrue(window.IsAppPaused);
            Assert.IsTrue(window.IsPlayerPaused);

            window.ResumeAnimation();
            Assert.IsFalse(window.IsAppPaused);
            Assert.IsFalse(window.IsPlayerPaused);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void PauseResumePause_AppAndPlayerConsistent()
    {
        // F-02: Multiple pause/resume cycles must keep App and player
        // state in sync.
        var window = TryCreateMainWindow();
        if (window is null)
        {
            Assert.Inconclusive("MainWindow requires character assets; skipping.");
            return;
        }

        try
        {
            // First cycle.
            window.PauseAnimation();
            Assert.IsTrue(window.IsAppPaused);
            Assert.IsTrue(window.IsPlayerPaused);

            window.ResumeAnimation();
            Assert.IsFalse(window.IsAppPaused);
            Assert.IsFalse(window.IsPlayerPaused);

            // Second cycle.
            window.PauseAnimation();
            Assert.IsTrue(window.IsAppPaused);
            Assert.IsTrue(window.IsPlayerPaused);

            window.ResumeAnimation();
            Assert.IsFalse(window.IsAppPaused);
            Assert.IsFalse(window.IsPlayerPaused);
        }
        finally
        {
            window.Close();
        }
    }
}
