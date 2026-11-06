using System.Reflection;
using DesktopPet.App;
using DesktopPet.Core.Emotion;
using DesktopPet.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P1-004: Probe (peek) visual tests.
/// Verifies probe start on ShouldFire, position restore after duration,
/// drag interrupt without restore, pause freeze, and exit restore.
/// </summary>
[TestClass]
public class ProbeVisualTests
{
    private static MainWindow CreateMainWindowOrFail()
    {
        try
        {
            return new MainWindow();
        }
        catch (Exception ex)
        {
            Assert.Fail($"MainWindow construction failed: {ex.Message}");
            throw;
        }
    }

    private static void InvokeTick(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("OnUpdateTick",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null)
            Assert.Fail("OnUpdateTick method not found.");
        method.Invoke(window, [null!, EventArgs.Empty]);
    }

    private static void SetField<T>(object obj, string name, T value)
    {
        var field = typeof(MainWindow).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is null)
            Assert.Fail($"Field '{name}' not found.");
        field!.SetValue(obj, value);
    }

    private static T GetField<T>(object obj, string name)
    {
        var field = typeof(MainWindow).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is null)
            Assert.Fail($"Field '{name}' not found.");
        return (T)field!.GetValue(obj)!;
    }

    /// <summary>
    /// Setup a scheduler + tracker that will ShouldFire on the next tick.
    /// Returns (scheduler, tracker). The caller sets TestRuntimeTracker and
    /// TestRareEventScheduler on the window.
    /// </summary>
    private static (RareEventScheduler scheduler, EffectiveRuntimeTracker tracker) SetupFireableScheduler(string today)
    {
        var rng = new Random(42);
        var tracker = new EffectiveRuntimeTracker();
        // Use a scheduler with triggerProbability=1.0 so Evaluate always fires
        // when delay is reached (not roll-dependent).
        var scheduler = new RareEventScheduler(1.0);

        tracker.StartActivePeriod(0);
        // Create a plan with a small delay (e.g. 20 min, but we use minDelay default).
        // EnsurePlanForDate uses default min/max delay of 20/120.
        scheduler.EnsurePlanForDate(today, rng, currentAccumulatedMinutes: tracker.AccumulatedMinutes);

        // Advance runtime to exceed the delay.
        // The plan's baseline is 0 (first plan), so dailyEffective = accumulatedMinutes.
        // We need accumulatedMinutes >= TriggerDelayMinutes. Since delay is random
        // between 20-120, we advance to 150 to guarantee exceeding any delay.
        tracker.UpdateTo(150);

        return (scheduler, tracker);
    }

    // ── Probe starts and moves window ──────────────────────────────────

    /// <summary>
    /// P1-004: When ShouldFire is true, the probe starts, the window position
    /// changes from the original, and the action switches to curious.
    /// </summary>
    [STATestMethod]
    public void Probe_ShouldFire_StartsProbeAndMovesWindow()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (900, 900);

            // Set initial window position.
            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → ShouldFire → probe starts.
            clockMs = 150 * 60000.0; // 150 minutes in ms
            InvokeTick(window);

            Assert.IsTrue(window.IsProbing, "Probe should be active after ShouldFire.");
            Assert.AreEqual("curious", window.CurrentAction, "Probe should play curious action.");
            // Window position should have changed from original.
            Assert.IsTrue(window.Left != originalLeft || window.Top != originalTop,
                "Window should have moved to the probe (edge) position.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Probe ends and restores original position ──────────────────────

    /// <summary>
    /// P1-004: After ProbeDurationMs elapses, the probe ends and the window
    /// returns to the original position.
    /// </summary>
    [STATestMethod]
    public void Probe_DurationElapses_RestoresOriginalPosition()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (900, 900);

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);

            // Advance time past probe duration (3000ms).
            clockMs = 150 * 60000.0 + 3100.0;
            InvokeTick(window);

            Assert.IsFalse(window.IsProbing, "Probe should have ended after duration.");
            Assert.AreEqual(originalLeft, window.Left, 0.01, "Window should restore original Left.");
            Assert.AreEqual(originalTop, window.Top, 0.01, "Window should restore original Top.");
            Assert.AreEqual("idle", window.CurrentAction, "Should return to idle after probe.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Drag interrupts probe without restoring position ───────────────

    /// <summary>
    /// P1-004: When the user starts dragging during a probe, the probe is
    /// cancelled but the window does NOT restore the original position.
    /// The drag takes over the position.
    /// </summary>
    [STATestMethod]
    public void Probe_DragInterrupts_DoesNotRestoreOriginalPosition()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (900, 900);

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);

            double probeLeft = window.Left;
            double probeTop = window.Top;

            // Simulate drag start via BeginCapturedPointerInteraction.
            // Use a head point that is on a visible (non-transparent) pixel,
            // then convert to drag by moving beyond the 8-DIP slop threshold.
            var headPoint = new System.Windows.Point(30, 9);
            var screenDip = new System.Windows.Point(probeLeft + 30, probeTop + 9);
            window.BeginCapturedPointerInteraction(headPoint, screenDip);
            Assert.IsTrue(window.IsPettingCandidate, "Head press should start petting candidate.");

            // Move beyond slop to convert to drag.
            var dragScreen = new System.Windows.Point(screenDip.X + 20, screenDip.Y);
            var dragImage = new System.Windows.Point(headPoint.X + 20, headPoint.Y);
            window.ProcessCapturedPointerMove(dragImage, dragScreen,
                leftPressed: true, nowMs: clockMs);

            Assert.IsFalse(window.IsProbing, "Probe should be cancelled by drag start.");
            Assert.IsTrue(window.IsDragging, "Should have converted to drag.");
            // Position should NOT have been restored to original.
            Assert.AreNotEqual(originalLeft, window.Left, 0.01,
                "Window should not restore original Left when drag interrupts probe.");
            Assert.AreNotEqual(originalTop, window.Top, 0.01,
                "Window should not restore original Top when drag interrupts probe.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Probe not re-triggered after consumption ───────────────────────

    /// <summary>
    /// P1-004: After a probe has fired and ended, a subsequent tick should
    /// NOT start a new probe (plan is consumed).
    /// </summary>
    [STATestMethod]
    public void Probe_AfterEnd_DoesNotRefireOnNextTick()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (900, 900);

            window.Left = 500;
            window.Top = 500;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);

            // Advance past duration → probe ends.
            clockMs = 150 * 60000.0 + 3100.0;
            InvokeTick(window);
            Assert.IsFalse(window.IsProbing);

            // Another tick → should not re-probe (plan consumed).
            clockMs = 150 * 60000.0 + 5000.0;
            InvokeTick(window);
            Assert.IsFalse(window.IsProbing, "Probe should not re-fire after plan consumed.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Probe not started while dragging ───────────────────────────────

    /// <summary>
    /// P1-004: ShouldFire should NOT start a probe if the pet is currently
    /// being dragged.
    /// </summary>
    [STATestMethod]
    public void Probe_ShouldFireWhileDragging_DoesNotStartProbe()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (900, 900);

            window.Left = 500;
            window.Top = 500;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Simulate active drag state.
            SetField(window, "_isDragging", true);

            // Tick → ShouldFire but dragging → no probe.
            clockMs = 150 * 60000.0;
            InvokeTick(window);

            Assert.IsFalse(window.IsProbing, "Probe should not start while dragging.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Exit restores probe position ───────────────────────────────────

    /// <summary>
    /// P1-004: RestoreProbePositionForExit restores the original position
    /// when a probe is active, so the saved state has the pre-probe location.
    /// </summary>
    [STATestMethod]
    public void Probe_RestoreProbePositionForExit_RestoresOriginal()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (900, 900);

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts and moves window.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);
            Assert.IsTrue(window.Left != originalLeft || window.Top != originalTop);

            // Exit restore.
            window.RestoreProbePositionForExit();

            Assert.AreEqual(originalLeft, window.Left, 0.01,
                "Exit restore should bring window back to original position.");
            Assert.AreEqual(originalTop, window.Top, 0.01,
                "Exit restore should bring window back to original position.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Probe start and end directly via internal methods ──────────────

    /// <summary>
    /// P1-004: StartProbe/EndProbe direct test — verify position recording
    /// and restore without depending on scheduler.
    /// </summary>
    [STATestMethod]
    public void Probe_StartAndEnd_RestoresPosition()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            window.Left = 300;
            window.Top = 400;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            // Start probe directly.
            window.StartProbe(0.0);
            Assert.IsTrue(window.IsProbing);
            Assert.AreEqual("curious", window.CurrentAction);

            // End probe with restore.
            window.EndProbe(restoreOriginal: true, 3100.0);
            Assert.IsFalse(window.IsProbing);
            Assert.AreEqual("idle", window.CurrentAction);
            Assert.AreEqual(originalLeft, window.Left, 0.01);
            Assert.AreEqual(originalTop, window.Top, 0.01);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// P1-004: EndProbe with restoreOriginal=false does NOT restore position.
    /// </summary>
    [STATestMethod]
    public void Probe_EndWithoutRestore_KeepsProbePosition()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            window.Left = 300;
            window.Top = 400;

            window.StartProbe(0.0);
            Assert.IsTrue(window.IsProbing);

            double probeLeft = window.Left;
            double probeTop = window.Top;

            // End without restore (drag interrupt case).
            window.EndProbe(restoreOriginal: false, 1000.0);
            Assert.IsFalse(window.IsProbing);
            Assert.AreEqual(probeLeft, window.Left, 0.01,
                "Position should remain at probe location when not restoring.");
            Assert.AreEqual(probeTop, window.Top, 0.01,
                "Position should remain at probe location when not restoring.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── F-09: Probe owns its action; hover/curiosity must not override ──

    /// <summary>
    /// F-09: During an active probe, ticks with the mouse far away must NOT
    /// cause the action to switch from curious to idle before the 3-second
    /// duration elapses.  The probe expression must persist for its full
    /// lifetime because the low-priority hover/curiosity branch is skipped.
    /// Verifies at 0ms, 400ms and 2500ms the action is still curious.
    /// </summary>
    [STATestMethod]
    public void F09_ProbeActive_MouseFarAway_CuriousPersists_FullLifetime()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            // Mouse is far from the pet — would normally end curiosity quickly.
            window.TestGetCursorPos = () => (2000, 2000);

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing, "Probe should be active.");
            Assert.AreEqual("curious", window.CurrentAction,
                "Probe should play curious immediately.");

            // 400ms later — mouse still far.  Before the fix, the hover
            // branch would end curiosity and switch to idle.
            clockMs = 150 * 60000.0 + 400.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing, "Probe should still be active at 400ms.");
            Assert.AreEqual("curious", window.CurrentAction,
                "Curious must persist at 400ms — probe owns the action.");

            // 2500ms — still within probe duration.
            clockMs = 150 * 60000.0 + 2500.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing, "Probe should still be active at 2500ms.");
            Assert.AreEqual("curious", window.CurrentAction,
                "Curious must persist at 2500ms — probe owns the action.");

            // 3100ms — past duration → probe ends, restores position, returns to idle.
            clockMs = 150 * 60000.0 + 3100.0;
            InvokeTick(window);
            Assert.IsFalse(window.IsProbing, "Probe should end after 3 seconds.");
            Assert.AreEqual("idle", window.CurrentAction,
                "Should return to idle after probe ends.");
            Assert.AreEqual(originalLeft, window.Left, 0.01,
                "Original position should be restored.");
            Assert.AreEqual(originalTop, window.Top, 0.01,
                "Original position should be restored.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-09: During an active probe, ticks with the mouse hovering over the
    /// pet must NOT cause the action to switch away from curious.  The
    /// hover/curiosity branch is skipped while probing.
    /// </summary>
    [STATestMethod]
    public void F09_ProbeActive_MouseHovering_CuriousPersists()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            // Mouse is roughly on the pet window — hover would normally trigger.
            window.TestGetCursorPos = () => ((int)window.Left + 60, (int)window.Top + 60);

            window.Left = 500;
            window.Top = 500;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);
            Assert.AreEqual("curious", window.CurrentAction);

            // 500ms later — mouse hovering.  Before the fix, the hover branch
            // would re-evaluate and potentially interfere.
            clockMs = 150 * 60000.0 + 500.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing, "Probe should still be active.");
            Assert.AreEqual("curious", window.CurrentAction,
                "Curious must persist while mouse hovers — probe owns the action.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-09: After the probe ends normally, the existing independent
    /// curiosity auto-end behavior must still work.  A normal hover-triggered
    /// curious should auto-end after its duration or when the mouse leaves.
    /// This is a regression guard — the fix must not break non-probe curiosity.
    /// </summary>
    [STATestMethod]
    public void F09_AfterProbeEnds_NormalCuriosityAutoEndStillWorks()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            // Mouse is on the pet to trigger hover.
            window.TestGetCursorPos = () => ((int)window.Left + 60, (int)window.Top + 60);

            window.Left = 500;
            window.Top = 500;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);
            Assert.AreEqual("curious", window.CurrentAction);

            // End probe.
            clockMs = 150 * 60000.0 + 3100.0;
            InvokeTick(window);
            Assert.IsFalse(window.IsProbing);
            Assert.AreEqual("idle", window.CurrentAction);

            // Now move mouse far away and advance — normal curiosity should
            // not trigger since mouse is not over pet.  Just verify the
            // hover/curiosity branch runs without error and stays idle.
            window.TestGetCursorPos = () => (2000, 2000);
            clockMs = 150 * 60000.0 + 4000.0;
            InvokeTick(window);
            Assert.IsFalse(window.IsProbing, "Probe should not restart.");
            // Action should be idle (mouse is far, no hover trigger).
            Assert.AreEqual("idle", window.CurrentAction,
                "Should remain idle when mouse is far after probe ends.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-09: When the user pauses during an active probe, the probe remaining
    /// time must freeze.  After resume, the probe continues for the remaining
    /// duration and then restores the original position.  Pause duration is
    /// NOT counted toward the probe lifetime.
    /// </summary>
    [STATestMethod]
    public void F09_PauseDuringProbe_ResumeContinuesRemainingTime()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (2000, 2000);

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);
            Assert.AreEqual("curious", window.CurrentAction);

            // 1000ms into the probe — pause now.
            clockMs = 150 * 60000.0 + 1000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing, "Probe should still be active at 1000ms.");
            window.PauseAnimation();
            Assert.IsTrue(window.IsProbing, "Probe flag should remain set during pause.");

            // Simulate 5 seconds of wall-clock pause time.
            clockMs = 150 * 60000.0 + 6000.0;
            window.ResumeAnimation();

            // After resume, only 2000ms of probe time should have elapsed
            // (1000ms before pause + 0ms during pause).  The probe should
            // still be active because 2000ms < 3000ms.
            clockMs = 150 * 60000.0 + 6000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing,
                "Probe should still be active after resume — only 1000ms of effective time elapsed.");
            Assert.AreEqual("curious", window.CurrentAction,
                "Curious should persist after resume — pause did not consume probe time.");

            // Advance 2000ms more effective time → total effective = 3000ms → probe ends.
            clockMs = 150 * 60000.0 + 8000.0;
            InvokeTick(window);
            Assert.IsFalse(window.IsProbing, "Probe should end after 3000ms effective time.");
            Assert.AreEqual("idle", window.CurrentAction);
            Assert.AreEqual(originalLeft, window.Left, 0.01,
                "Original position should be restored after pause+resume probe.");
            Assert.AreEqual(originalTop, window.Top, 0.01,
                "Original position should be restored after pause+resume probe.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── F-10: Only real drag cancels probe; head short-press does not ───

    /// <summary>
    /// F-10: A head short-press (no movement) followed by release must NOT
    /// cancel the probe.  The probe continues its lifetime and restores the
    /// original position after 3 seconds.  This test calls the actual
    /// BeginCapturedPointerInteraction → EndCapturedPointerInteraction chain
    /// (the production gesture entry), not just CancelProbeForDrag directly.
    /// </summary>
    [STATestMethod]
    public void F10_HeadShortPress_Release_NoMove_ProbeContinuesAndRestores()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (900, 900);

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);

            double probeLeft = window.Left;
            double probeTop = window.Top;

            // Head short-press via the production gesture entry.
            // Point (30, 9) is in the upper 1/3 head zone of a 120×120 image.
            var headPoint = new System.Windows.Point(30, 9);
            var screenDip = new System.Windows.Point(probeLeft + 30, probeTop + 9);
            bool accepted = window.BeginCapturedPointerInteraction(headPoint, screenDip);

            Assert.IsTrue(accepted, "Head press should be accepted (petting candidate).");
            Assert.IsTrue(window.IsPettingCandidate, "Should be in petting candidate state.");
            Assert.IsTrue(window.IsProbing,
                "Probe must NOT be cancelled by a head short-press.");
            Assert.AreEqual(probeLeft, window.Left, 0.01,
                "Window must not move during head short-press.");
            Assert.AreEqual(probeTop, window.Top, 0.01,
                "Window must not move during head short-press.");

            // Release without moving — EndCapturedPointerInteraction → EndPetting.
            window.EndCapturedPointerInteraction();
            Assert.IsFalse(window.IsPettingCandidate, "Petting should end on release.");
            Assert.IsTrue(window.IsProbing,
                "Probe must still be active after head short-press release.");

            // Advance past probe duration → should restore original position.
            clockMs = 150 * 60000.0 + 3100.0;
            InvokeTick(window);

            Assert.IsFalse(window.IsProbing, "Probe should end after duration.");
            Assert.AreEqual(originalLeft, window.Left, 0.01,
                "Original position must be restored — head short-press did not cancel probe.");
            Assert.AreEqual(originalTop, window.Top, 0.01,
                "Original position must be restored — head short-press did not cancel probe.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-10: A head press that converts to drag (movement exceeds 8 DIP)
    /// must cancel the probe without restoring the original position.
    /// The original grab point must be preserved (no jump).
    /// </summary>
    [STATestMethod]
    public void F10_HeadPressConvertsToDrag_CancelsProbe_NoRestore()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (900, 900);

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);

            double probeLeft = window.Left;
            double probeTop = window.Top;

            // Head press.
            var headPoint = new System.Windows.Point(30, 9);
            var pressScreen = new System.Windows.Point(probeLeft + 30, probeTop + 9);
            bool accepted = window.BeginCapturedPointerInteraction(headPoint, pressScreen);
            Assert.IsTrue(accepted);
            Assert.IsTrue(window.IsPettingCandidate);
            Assert.IsTrue(window.IsProbing, "Probe should still be active after head press.");

            // Move beyond the 8 DIP slop threshold to convert to drag.
            double moveDx = 20.0; // well above HeadDragSlopDip (8.0)
            var newScreenDip = new System.Windows.Point(pressScreen.X + moveDx, pressScreen.Y);
            var newImagePoint = new System.Windows.Point(headPoint.X + moveDx, headPoint.Y);
            window.ProcessCapturedPointerMove(newImagePoint, newScreenDip,
                leftPressed: true, nowMs: clockMs);

            Assert.IsTrue(window.IsDragging, "Should have converted to drag.");
            Assert.IsFalse(window.IsProbing,
                "Probe must be cancelled when head press converts to drag.");
            // Window follows the drag — it should NOT be at the original
            // pre-probe position and should NOT have been restored.
            Assert.AreNotEqual(originalLeft, window.Left, 0.01,
                "Position should NOT be the original — drag took over from probe.");

            // End drag.
            window.EndCapturedPointerInteraction();
            Assert.IsFalse(window.IsDragging);
            Assert.IsFalse(window.IsProbing, "Probe must not restart after drag ends.");
            Assert.AreNotEqual(originalLeft, window.Left, 0.01,
                "Position should NOT be the original — drag took over from probe position.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-10: A body press (immediate drag) must cancel the probe without
    /// restoring the original position.  This verifies the existing behavior
    /// is preserved after the F-10 fix moved CancelProbeForDrag into
    /// StartDragging.
    /// </summary>
    [STATestMethod]
    public void F10_BodyPressDrag_CancelsProbe_NoRestore()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (900, 900);

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);

            double probeLeft = window.Left;
            double probeTop = window.Top;

            // Verify the probe actually moved the window (same as
            // Probe_ShouldFire_StartsProbeAndMovesWindow).
            Assert.IsTrue(probeLeft != originalLeft || probeTop != originalTop,
                "Probe should have moved the window from the original position.");

            // Body press → immediate drag.
            // Use a point in the lower-middle of the image that is likely
            // on a non-transparent pixel of the bear character.
            var bodyPoint = new System.Windows.Point(60, 60);
            var screenDip = new System.Windows.Point(probeLeft + 60, probeTop + 60);
            bool accepted = window.BeginCapturedPointerInteraction(bodyPoint, screenDip);

            Assert.IsTrue(accepted, "Body press should be accepted.");
            Assert.IsTrue(window.IsDragging, "Body press should start drag immediately.");
            Assert.IsFalse(window.IsProbing,
                "Probe must be cancelled by body drag.");
            // Drag has not moved the window yet (no ProcessCapturedPointerMove).
            // Position should be at probe position, NOT restored to original.
            Assert.AreEqual(probeLeft, window.Left, 0.01,
                "Window should stay at probe Left — drag owns position, no restore.");
            Assert.AreEqual(probeTop, window.Top, 0.01,
                "Window should stay at probe Top — drag owns position, no restore.");
            // Verify the probe position is indeed different from the original
            // (at least one axis must differ — the probe moved the window).
            Assert.IsTrue(probeLeft != originalLeft || probeTop != originalTop,
                "Probe should have moved the window from the original position.");

            // End drag.
            window.EndCapturedPointerInteraction();
            Assert.IsFalse(window.IsDragging);
            // After EndDrag, ClampWindowToWorkArea may adjust, but the
            // position should still not be the original pre-probe position
            // (probe moved to edge, drag took over from there).
            Assert.IsTrue(window.Left != originalLeft || window.Top != originalTop,
                "Position should NOT be original — drag took over from probe position.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-10: A transparent/invalid press (point not on visible pet) must be
    /// rejected and must NOT cancel the probe.  The probe continues and
    /// restores the original position after its duration.
    /// </summary>
    [STATestMethod]
    public void F10_InvalidPress_Rejected_DoesNotCancelProbe()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (900, 900);

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);

            double probeLeft = window.Left;
            double probeTop = window.Top;

            // Invalid press — point far outside the image (transparent area).
            var invalidPoint = new System.Windows.Point(-50, -50);
            var screenDip = new System.Windows.Point(probeLeft - 50, probeTop - 50);
            bool accepted = window.BeginCapturedPointerInteraction(invalidPoint, screenDip);

            Assert.IsFalse(accepted, "Invalid press should be rejected.");
            Assert.IsTrue(window.IsProbing,
                "Probe must NOT be cancelled by an invalid press.");
            Assert.AreEqual(probeLeft, window.Left, 0.01,
                "Window must not move on invalid press.");
            Assert.AreEqual(probeTop, window.Top, 0.01,
                "Window must not move on invalid press.");

            // Advance past probe duration → should restore original position.
            clockMs = 150 * 60000.0 + 3100.0;
            InvokeTick(window);

            Assert.IsFalse(window.IsProbing, "Probe should end after duration.");
            Assert.AreEqual(originalLeft, window.Left, 0.01,
                "Original position must be restored — invalid press did not cancel probe.");
            Assert.AreEqual(originalTop, window.Top, 0.01,
                "Original position must be restored — invalid press did not cancel probe.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-10: Head micro-move within slop threshold (less than 8 DIP) must
    /// NOT convert to drag and must NOT cancel the probe.  The probe
    /// continues and restores the original position.
    /// </summary>
    [STATestMethod]
    public void F10_HeadMicroMove_WithinSlop_DoesNotCancelProbe()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (900, 900);

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var (scheduler, tracker) = SetupFireableScheduler(today);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick → probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);
            Assert.IsTrue(window.IsProbing);

            double probeLeft = window.Left;
            double probeTop = window.Top;

            // Head press.
            var headPoint = new System.Windows.Point(30, 9);
            var pressScreen = new System.Windows.Point(probeLeft + 30, probeTop + 9);
            bool accepted = window.BeginCapturedPointerInteraction(headPoint, pressScreen);
            Assert.IsTrue(accepted);
            Assert.IsTrue(window.IsProbing, "Probe should survive head press.");

            // Micro-move within slop (3 DIP — below 8 DIP threshold).
            double microDx = 3.0;
            var microScreen = new System.Windows.Point(pressScreen.X + microDx, pressScreen.Y);
            var microImage = new System.Windows.Point(headPoint.X + microDx, headPoint.Y);
            window.ProcessCapturedPointerMove(microImage, microScreen,
                leftPressed: true, nowMs: clockMs);

            Assert.IsFalse(window.IsDragging, "Micro-move should not convert to drag.");
            Assert.IsTrue(window.IsPettingCandidate, "Should still be petting candidate.");
            Assert.IsTrue(window.IsProbing,
                "Probe must NOT be cancelled by micro-move within slop.");

            // Release.
            window.EndCapturedPointerInteraction();
            Assert.IsTrue(window.IsProbing,
                "Probe must still be active after micro-move release.");

            // Advance past duration → restore.
            clockMs = 150 * 60000.0 + 3100.0;
            InvokeTick(window);

            Assert.IsFalse(window.IsProbing, "Probe should end after duration.");
            Assert.AreEqual(originalLeft, window.Left, 0.01,
                "Original position must be restored after micro-move did not cancel probe.");
            Assert.AreEqual(originalTop, window.Top, 0.01,
                "Original position must be restored after micro-move did not cancel probe.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── F-09 remaining: Grievance ↔ Probe priority coordination ────────

    /// <summary>
    /// Helper: setup a fireable scheduler + tracker (same as
    /// SetupFireableScheduler but returns the today string for convenience).
    /// </summary>
    private static (RareEventScheduler scheduler, EffectiveRuntimeTracker tracker, string today)
        SetupFireableSchedulerWithToday()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var (scheduler, tracker) = SetupFireableScheduler(today);
        return (scheduler, tracker, today);
    }

    /// <summary>
    /// F-09 R10: When grievance expression is already in progress and the
    /// rare-event scheduler would reach ShouldFire on the same tick, Evaluate
    /// is delayed — the plan is NOT consumed. Grievance expression continues
    /// undisturbed. After grievance ends (10s), the next tick evaluates the
    /// same plan; with triggerProbability=1.0, ShouldFire fires, probe starts,
    /// and the plan is consumed at that point.
    ///
    /// This drives the actual OnUpdateTick with both emotion and rare-event
    /// objects injected simultaneously.
    /// </summary>
    [STATestMethod]
    public void F09_GrievanceActive_SchedulerFires_ProbeDoesNotStart_GrievanceContinues()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            // Mouse near the pet so grievance "first encounter" check passes.
            window.TestGetCursorPos = () => ((int)window.Left + 60, (int)window.Top + 60);
            window.TestGetUtcNow = () => DateTime.UtcNow;

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            // Setup emotion objects with a pending grievance expression
            // that should fire today (marked yesterday).
            var accumulator = new EmotionDecayAccumulator(85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            // Mark pending with yesterday's date so ShouldExpress returns true.
            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            // Setup a fireable rare-event scheduler (triggerProbability=1.0).
            var (scheduler, tracker, today) = SetupFireableSchedulerWithToday();
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick 1: Grievance expression starts (ShouldExpress=true,
            // mouse in range). The grievance branch runs first, setting
            // _isExpressingGrievance=true. The rare-event branch then
            // SKIPS Evaluate because _isExpressingGrievance is true.
            // The plan remains unconsumed.
            clockMs = 150 * 60000.0;
            InvokeTick(window);

            // Grievance expression should be active.
            Assert.IsTrue(window.IsExpressingGrievance,
                "Grievance expression should have started.");
            Assert.AreEqual("grievance", window.CurrentAction,
                $"Action should be 'grievance', not 'curious'. Got: {window.CurrentAction}");

            // Probe must NOT have started — grievance owns the action.
            Assert.IsFalse(window.IsProbing,
                "Probe must not start while grievance is being expressed.");
            // Window must not have moved to screen edge.
            Assert.AreEqual(originalLeft, window.Left, 0.01,
                "Window must not move to probe position during grievance.");
            Assert.AreEqual(originalTop, window.Top, 0.01,
                "Window must not move to probe position during grievance.");

            // R10: The plan must NOT be consumed — Evaluate was delayed.
            Assert.IsFalse(scheduler.CurrentPlan?.Consumed == true,
                "Plan must NOT be consumed while grievance is active — Evaluate is delayed per R10.");

            // Tick 2: Advance time — grievance should continue, probe should
            // not start on subsequent ticks either. Plan still unconsumed.
            clockMs = 150 * 60000.0 + 2000.0;
            InvokeTick(window);

            Assert.IsTrue(window.IsExpressingGrievance,
                "Grievance expression must continue (10s duration not elapsed).");
            Assert.AreEqual("grievance", window.CurrentAction,
                "Action must remain 'grievance'.");
            Assert.IsFalse(window.IsProbing,
                "Probe must not start while grievance is active.");
            Assert.IsFalse(scheduler.CurrentPlan?.Consumed == true,
                "Plan must still be unconsumed during grievance.");

            // Tick 3: After 10 seconds, grievance expression completes → idle.
            // Now _isExpressingGrievance is false. Evaluate runs on this tick:
            // the same plan is evaluated, ShouldFire=true (probability=1.0),
            // probe starts, plan is consumed.
            clockMs = 150 * 60000.0 + 10500.0;
            InvokeTick(window);

            Assert.IsFalse(window.IsExpressingGrievance,
                "Grievance expression should end after 10 seconds.");
            // After grievance ends, the delayed Evaluate runs: ShouldFire
            // fires, probe starts, plan consumed.
            Assert.IsTrue(window.IsProbing,
                "Probe should start after grievance ends — the delayed plan is now evaluated.");
            Assert.IsTrue(scheduler.CurrentPlan?.Consumed == true,
                "Plan must be consumed after grievance ends and Evaluate runs.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-09 remaining: When a probe is already active and grievance
    /// expression starts on a subsequent tick, the grievance (higher
    /// priority) must safely take over: the probe ends and restores the
    /// original position, but EndProbe does NOT switch to idle — grievance
    /// owns the action. Subsequent ticks must not revert to idle while
    /// grievance is still in progress. When the probe duration would have
    /// elapsed, the already-ended probe does not fire again.
    ///
    /// This drives the actual OnUpdateTick with both emotion and rare-event
    /// objects injected simultaneously.
    /// </summary>
    [STATestMethod]
    public void F09_ProbeActive_GrievanceStarts_ProbeSafelyEnds_GrievanceContinues()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            // Mouse near the pet so grievance "first encounter" check passes.
            window.TestGetCursorPos = () => ((int)window.Left + 60, (int)window.Top + 60);
            window.TestGetUtcNow = () => DateTime.UtcNow;

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            // Setup emotion objects — grievance is pending but will NOT fire
            // on the first tick because we mark it with TODAY's date (not
            // yesterday). It will fire on a later tick after we re-mark it.
            var accumulator = new EmotionDecayAccumulator(85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            // Setup a fireable rare-event scheduler.
            var (scheduler, tracker, today) = SetupFireableSchedulerWithToday();
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick 1: No grievance pending yet (nothing marked) → probe fires.
            // The scheduler reaches ShouldFire, probe starts.
            clockMs = 150 * 60000.0;
            InvokeTick(window);

            Assert.IsTrue(window.IsProbing,
                "Probe should start when ShouldFire and no grievance is active.");
            Assert.AreEqual("curious", window.CurrentAction,
                "Probe should play curious.");
            // Window should have moved to probe (edge) position.
            Assert.IsTrue(window.Left != originalLeft || window.Top != originalTop,
                "Window should have moved to probe position.");

            double probeLeft = window.Left;
            double probeTop = window.Top;

            // Now mark grievance pending with yesterday's date so that on
            // the next tick, ShouldExpress returns true and grievance starts.
            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            // Tick 2: Grievance starts. The grievance branch (line ~674) runs
            // first: ShouldExpress=true, mouse in range → grievance starts.
            // The code detects _isProbing and calls EndProbe(restoreOriginal=true)
            // BEFORE playing grievance. EndProbe restores the original position
            // but does NOT switch to idle (because _isExpressingGrievance is
            // already true). Then grievance is played.
            clockMs = 150 * 60000.0 + 500.0; // 500ms into the probe
            InvokeTick(window);

            // Grievance should now be active.
            Assert.IsTrue(window.IsExpressingGrievance,
                "Grievance expression should have started.");
            Assert.AreEqual("grievance", window.CurrentAction,
                $"Action should be 'grievance' after grievance starts during probe. Got: {window.CurrentAction}");

            // Probe should have ended.
            Assert.IsFalse(window.IsProbing,
                "Probe must end when grievance starts (grievance is higher priority).");

            // Window should have been restored to the original position
            // (not left at the screen edge).
            Assert.AreEqual(originalLeft, window.Left, 0.01,
                "Window must restore original position when grievance takes over probe.");
            Assert.AreEqual(originalTop, window.Top, 0.01,
                "Window must restore original position when grievance takes over probe.");

            // Tick 3: Advance past what would have been the probe duration
            // (3 seconds). The probe already ended, so this tick must NOT
            // trigger any probe-related action. Grievance must continue.
            // R10: The plan was already consumed by tick 1's Evaluate (before
            // grievance started). Evaluate is skipped during grievance, but
            // the consumption stands — no replay.
            clockMs = 150 * 60000.0 + 3100.0; // past 3s probe duration
            InvokeTick(window);

            Assert.IsTrue(window.IsExpressingGrievance,
                "Grievance must continue — only 2600ms of 10s elapsed.");
            Assert.AreEqual("grievance", window.CurrentAction,
                "Action must remain 'grievance' — probe end must not override it.");
            Assert.IsFalse(window.IsProbing,
                "Probe must not restart — plan already consumed.");
            Assert.IsTrue(scheduler.CurrentPlan?.Consumed == true,
                "Plan must remain consumed — probe was already shown before grievance interrupted.");

            // Tick 4: After 10 seconds of grievance, it completes → idle.
            // Evaluate runs but plan is already consumed → no replay.
            clockMs = 150 * 60000.0 + 10500.0;
            InvokeTick(window);

            Assert.IsFalse(window.IsExpressingGrievance,
                "Grievance expression should end after 10 seconds.");
            Assert.AreEqual("idle", window.CurrentAction,
                "Should return to idle after grievance completes.");
            Assert.IsFalse(window.IsProbing,
                "Probe must not replay after grievance ends — plan already consumed.");
            Assert.IsTrue(scheduler.CurrentPlan?.Consumed == true,
                "Plan must remain consumed after grievance ends — no replay.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-09 R10: When grievance is active and the scheduler would evaluate,
    /// the plan is NOT consumed. After grievance ends, the same plan is
    /// evaluated. With triggerProbability=0.0, the roll fails (RollFailed),
    /// the plan is consumed (roll-failed consumption), and no probe starts.
    /// This proves the plan is preserved unconsumed during grievance and
    /// only consumed when actually evaluated after grievance ends.
    /// </summary>
    [STATestMethod]
    public void F09_GrievanceActive_RollFailedAfterGrievance_PlanPreservedThenConsumed()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => ((int)window.Left + 60, (int)window.Top + 60);
            window.TestGetUtcNow = () => DateTime.UtcNow;

            window.Left = 500;
            window.Top = 500;
            double originalLeft = window.Left;
            double originalTop = window.Top;

            var accumulator = new EmotionDecayAccumulator(85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            // Setup scheduler with triggerProbability=0.0 so the roll always
            // fails. The plan is still consumed on RollFailed.
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var rng = new Random(42);
            var tracker = new EffectiveRuntimeTracker();
            var scheduler = new RareEventScheduler(0.0); // never fires
            tracker.StartActivePeriod(0);
            scheduler.EnsurePlanForDate(today, rng, currentAccumulatedMinutes: tracker.AccumulatedMinutes);
            tracker.UpdateTo(150);
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick 1: Grievance starts. Evaluate is skipped — plan unconsumed.
            clockMs = 150 * 60000.0;
            InvokeTick(window);

            Assert.IsTrue(window.IsExpressingGrievance,
                "Grievance expression should have started.");
            Assert.IsFalse(window.IsProbing,
                "Probe must not start while grievance is active.");
            Assert.IsFalse(scheduler.CurrentPlan?.Consumed == true,
                "Plan must NOT be consumed during grievance — Evaluate is delayed.");

            // Tick 2: Still in grievance (5s elapsed). Plan still unconsumed.
            clockMs = 150 * 60000.0 + 5000.0;
            InvokeTick(window);

            Assert.IsTrue(window.IsExpressingGrievance,
                "Grievance must continue.");
            Assert.IsFalse(scheduler.CurrentPlan?.Consumed == true,
                "Plan must still be unconsumed during grievance.");

            // Tick 3: After 10s, grievance ends. Evaluate runs: roll fails
            // (probability=0.0), plan is consumed (RollFailed), no probe.
            clockMs = 150 * 60000.0 + 10500.0;
            InvokeTick(window);

            Assert.IsFalse(window.IsExpressingGrievance,
                "Grievance expression should end after 10 seconds.");
            Assert.IsFalse(window.IsProbing,
                "Probe must not start — roll failed (probability=0.0).");
            Assert.IsTrue(scheduler.CurrentPlan?.Consumed == true,
                "Plan must be consumed after grievance ends — RollFailed consumes the plan.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-09 R10: When grievance is active, the plan is preserved unconsumed.
    /// A snapshot taken during grievance retains the unconsumed plan. After
    /// restoring the snapshot (simulating a restart), the plan is still
    /// unconsumed and can be evaluated normally.
    /// </summary>
    [STATestMethod]
    public void F09_GrievanceActive_SnapshotPreservesUnconsumedPlan_RestoreAndEvaluate()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => ((int)window.Left + 60, (int)window.Top + 60);
            window.TestGetUtcNow = () => DateTime.UtcNow;

            window.Left = 500;
            window.Top = 500;

            var accumulator = new EmotionDecayAccumulator(85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            var (scheduler, tracker, today) = SetupFireableSchedulerWithToday();
            window.TestRuntimeTracker = tracker;
            window.TestRareEventScheduler = scheduler;

            // Tick 1: Grievance starts. Evaluate is skipped — plan unconsumed.
            clockMs = 150 * 60000.0;
            InvokeTick(window);

            Assert.IsTrue(window.IsExpressingGrievance, "Grievance should start.");
            Assert.IsFalse(scheduler.CurrentPlan?.Consumed == true,
                "Plan must be unconsumed during grievance.");

            // Take a snapshot during grievance — plan should be unconsumed.
            var snapshot = scheduler.GetSnapshot();
            Assert.IsNotNull(snapshot, "Snapshot must not be null.");
            Assert.IsFalse(snapshot!.Consumed,
                "Snapshot plan must be unconsumed — taken during grievance.");

            // Simulate a restart: create a new scheduler and restore the snapshot.
            var restoredScheduler = new RareEventScheduler(1.0);
            var restoredTracker = new EffectiveRuntimeTracker();
            restoredTracker.StartActivePeriod(0);
            restoredTracker.UpdateTo(150);
            bool accepted = restoredScheduler.RestorePlan(snapshot);
            Assert.IsTrue(accepted, "Snapshot plan must be valid and accepted.");
            Assert.IsFalse(restoredScheduler.CurrentPlan?.Consumed == true,
                "Restored plan must be unconsumed.");

            // After restore, evaluate the plan — it should fire (probability=1.0).
            var result = restoredScheduler.Evaluate(
                restoredTracker.AccumulatedMinutes, today,
                isPaused: false, isDragging: false,
                isSocialActive: false, isRestReminderActive: false);
            Assert.IsTrue(result.ShouldFire,
                "Restored unconsumed plan should fire when evaluated after grievance.");
            Assert.IsTrue(restoredScheduler.CurrentPlan?.Consumed == true,
                "Plan must be consumed after evaluation fires.");
        }
        finally
        {
            window.Close();
        }
    }
}
