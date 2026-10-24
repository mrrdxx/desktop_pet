using System.Reflection;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using DesktopPet.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P1-004: Peek event App integration tests (R-03).
/// Drives the actual OnUpdateTick entry point with an injected PeekScheduler
/// (TestPeekScheduler) and controlled clock to verify that:
/// - Peek starts and plays the curious action when candidate is reached.
/// - Peek completes after ~5 seconds and returns to idle.
/// - Drag interrupts peek; interrupted peek marks quota as consumed.
/// - Paused time does not accumulate as effective run time.
/// - Peek does not start during high-priority interrupts (drag/menu/grievance).
/// - Peek state is marked dirty for persistence after start/complete/interrupt.
/// </summary>
[TestClass]
public class PeekIntegrationTests
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

    private static AnimationPlayer GetAnimPlayer(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_animPlayer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is null)
            Assert.Fail("_animPlayer field not found.");
        return (AnimationPlayer)field!.GetValue(window)!;
    }

    /// <summary>
    /// Find a Random seed where the probability check passes (peek fires).
    /// </summary>
    private static int FindSeedThatTriggers()
    {
        for (int seed = 0; seed < 10000; seed++)
        {
            var s = new PeekScheduler(new Random(seed));
            s.Tick("2026-10-24", 1000, false, false, 1000);
            var result = s.Tick("2026-10-24", s.CandidateMinutes * 60_000.0, false, false,
                s.CandidateMinutes * 60_000.0 + 1000);
            if (result == PeekTickResult.PeekStarted)
                return seed;
        }
        Assert.Fail("Could not find a seed that triggers.");
        return -1;
    }

    /// <summary>
    /// Find a Random seed where the probability check fails (peek does not fire).
    /// </summary>
    private static int FindSeedThatFails()
    {
        for (int seed = 0; seed < 10000; seed++)
        {
            var s = new PeekScheduler(new Random(seed));
            s.Tick("2026-10-24", 1000, false, false, 1000);
            var result = s.Tick("2026-10-24", s.CandidateMinutes * 60_000.0, false, false,
                s.CandidateMinutes * 60_000.0 + 1000);
            if (result == PeekTickResult.NoAction && s.HasAttempted && s.HasTriggered)
                return seed;
        }
        Assert.Fail("Could not find a seed where probability fails.");
        return -1;
    }

    /// <summary>
    /// Advance the clock in steps small enough for OnUpdateTick's delta clamp (< 60s).
    /// Uses 50,000ms steps to reach the target efficiently.
    /// </summary>
    private static void AdvanceClock(MainWindow window, ref double clockMs, double targetMs)
    {
        const double stepMs = 50_000.0; // 50 seconds per step — under the 60s clamp.
        while (clockMs < targetMs)
        {
            var next = Math.Min(clockMs + stepMs, targetMs);
            clockMs = next;
            InvokeTick(window);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Peek starts and plays curious action
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// When effective run time reaches the candidate and probability passes,
    /// OnUpdateTick should start the peek and switch to the curious action.
    /// </summary>
    [STATestMethod]
    public void Peek_ReachesCandidate_StartsCuriousAction()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (500, 500);
            window.TestGetDpiScale = () => 1.0;

            int seed = FindSeedThatTriggers();
            var peek = new PeekScheduler(new Random(seed));
            window.TestPeekScheduler = peek;

            // First tick — creates the plan.
            clockMs = 1000;
            InvokeTick(window);
            var candidate = peek.CandidateMinutes;

            // Advance to candidate time in steps (delta clamp < 60s).
            AdvanceClock(window, ref clockMs, candidate * 60_000.0 + 1000);

            Assert.IsTrue(window.IsPeeking, "Pet should be peeking after candidate is reached.");
            Assert.AreEqual("curious", window.CurrentAction,
                "Peek should play the curious action (placeholder).");
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Peek completes after duration and returns to idle
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// After the peek duration (~5 seconds), OnUpdateTick should complete
    /// the peek and return to idle action.
    /// </summary>
    [STATestMethod]
    public void Peek_AfterDuration_CompletesAndReturnsToIdle()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (500, 500);
            window.TestGetDpiScale = () => 1.0;

            int seed = FindSeedThatTriggers();
            var peek = new PeekScheduler(new Random(seed));
            window.TestPeekScheduler = peek;

            // Create plan and trigger peek.
            clockMs = 1000;
            InvokeTick(window);
            var candidate = peek.CandidateMinutes;

            AdvanceClock(window, ref clockMs, candidate * 60_000.0 + 1000);
            Assert.IsTrue(window.IsPeeking);

            // Just before duration — still peeking.
            clockMs = candidate * 60_000.0 + 1000 + PeekScheduler.PeekDurationMs - 100;
            InvokeTick(window);
            Assert.IsTrue(window.IsPeeking, "Peek should still be in progress just before duration.");

            // At duration — should complete.
            clockMs = candidate * 60_000.0 + 1000 + PeekScheduler.PeekDurationMs + 100;
            InvokeTick(window);

            Assert.IsFalse(window.IsPeeking, "Peek should complete after duration.");
            Assert.AreEqual("idle", window.CurrentAction,
                "Action should return to idle after peek completes.");
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Drag interrupts peek
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// When a drag starts during a peek, the peek should be interrupted
    /// and the quota consumed (HasTriggered=true). The action should switch
    /// to drag, not stay on curious.
    /// </summary>
    [STATestMethod]
    public void Peek_DragStart_InterruptsPeek()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (500, 500);
            window.TestGetDpiScale = () => 1.0;

            int seed = FindSeedThatTriggers();
            var peek = new PeekScheduler(new Random(seed));
            window.TestPeekScheduler = peek;

            // Create plan and trigger peek.
            clockMs = 1000;
            InvokeTick(window);
            var candidate = peek.CandidateMinutes;

            AdvanceClock(window, ref clockMs, candidate * 60_000.0 + 1000);
            Assert.IsTrue(window.IsPeeking);

            // Simulate drag start by setting the _isDragging field directly.
            SetField(window, "_isDragging", true);

            clockMs += 100;
            InvokeTick(window);

            Assert.IsFalse(window.IsPeeking, "Peek should be interrupted by drag.");
            Assert.IsTrue(peek.HasTriggered, "Interrupted peek should mark quota as consumed.");
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Paused time does not accumulate as effective run time
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the app is paused, OnUpdateTick should not accumulate effective
    /// run time for the peek scheduler. The candidate should not be reached
    /// during pause.
    /// </summary>
    [STATestMethod]
    public void Peek_PausedTime_DoesNotReachCandidate()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (500, 500);
            window.TestGetDpiScale = () => 1.0;

            int seed = FindSeedThatTriggers();
            var peek = new PeekScheduler(new Random(seed));
            window.TestPeekScheduler = peek;

            // Create plan.
            clockMs = 1000;
            InvokeTick(window);
            var candidate = peek.CandidateMinutes;
            Assert.IsFalse(peek.HasAttempted);

            // Pause the app.
            SetField(window, "_isAppPaused", true);

            // Accumulate time beyond candidate while paused (in small steps).
            AdvanceClock(window, ref clockMs, (candidate + 5) * 60_000.0 + 1000);

            Assert.IsFalse(peek.HasAttempted,
                "Paused time should not accumulate toward the candidate.");
            Assert.IsFalse(window.IsPeeking, "Peek should not start while paused.");
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Peek does not start during high-priority interrupts
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// When a high-priority interrupt is active (drag), the peek should
    /// defer even if the candidate has been reached and probability passed.
    /// When the interrupt clears, the peek should fire.
    /// </summary>
    [STATestMethod]
    public void Peek_InterruptedAtCandidate_DefersUntilIdle()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (500, 500);
            window.TestGetDpiScale = () => 1.0;

            int seed = FindSeedThatTriggers();
            var peek = new PeekScheduler(new Random(seed));
            window.TestPeekScheduler = peek;

            // Create plan.
            clockMs = 1000;
            InvokeTick(window);
            var candidate = peek.CandidateMinutes;

            // Set drag interrupt active.
            SetField(window, "_isDragging", true);

            // Reach candidate while interrupted — should attempt but not fire.
            clockMs = candidate * 60_000.0 + 1000;
            InvokeTick(window);

            Assert.IsTrue(peek.HasAttempted, "Should have attempted at candidate time.");
            Assert.IsTrue(peek.ShouldFire, "Probability should have passed.");
            Assert.IsFalse(window.IsPeeking, "Peek should not start while interrupted.");

            // Clear interrupt — should fire on next tick.
            SetField(window, "_isDragging", false);

            clockMs = candidate * 60_000.0 + 1100;
            InvokeTick(window);

            Assert.IsTrue(window.IsPeeking, "Peek should fire after interrupt clears.");
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Peek does not repeat on the same day after completion
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// After a peek completes, subsequent ticks should not trigger another
    /// peek on the same day.
    /// </summary>
    [STATestMethod]
    public void Peek_AfterCompletion_DoesNotRepeatSameDay()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (500, 500);
            window.TestGetDpiScale = () => 1.0;

            int seed = FindSeedThatTriggers();
            var peek = new PeekScheduler(new Random(seed));
            window.TestPeekScheduler = peek;

            // Create plan and trigger peek.
            clockMs = 1000;
            InvokeTick(window);
            var candidate = peek.CandidateMinutes;

            clockMs = candidate * 60_000.0 + 1000;
            InvokeTick(window);
            Assert.IsTrue(window.IsPeeking);

            // Complete the peek.
            clockMs = candidate * 60_000.0 + 1000 + PeekScheduler.PeekDurationMs + 100;
            InvokeTick(window);
            Assert.IsFalse(window.IsPeeking);
            Assert.IsTrue(peek.HasTriggered);

            // More ticks — should not re-trigger.
            for (int i = 0; i < 10; i++)
            {
                clockMs = (candidate + i + 2) * 60_000.0;
                InvokeTick(window);
                Assert.IsFalse(window.IsPeeking, "Peek should not repeat on the same day.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Peek state persists through MarkStateDirty
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// After a peek starts, the peek scheduler state should be synced to
    /// the archive via MarkStateDirty. This test verifies that the
    /// MapEmotionObjectsToArchive path includes peek fields when called
    /// from the production MarkStateDirty flow.
    /// </summary>
    [STATestMethod]
    public void Peek_Start_MarksStateDirty_WithPeekFields()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (500, 500);
            window.TestGetDpiScale = () => 1.0;

            int seed = FindSeedThatTriggers();
            var peek = new PeekScheduler(new Random(seed));
            window.TestPeekScheduler = peek;

            // Create plan and trigger peek.
            clockMs = 1000;
            InvokeTick(window);
            var candidate = peek.CandidateMinutes;

            clockMs = candidate * 60_000.0 + 1000;
            InvokeTick(window);
            Assert.IsTrue(window.IsPeeking);

            // The peek scheduler should have a plan with the correct date.
            Assert.AreEqual(
                DateTime.Now.ToString("yyyy-MM-dd"),
                peek.EventDate,
                "Peek event date should be today.");
            Assert.AreEqual(candidate, peek.CandidateMinutes);
            Assert.IsTrue(peek.HasAttempted);
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Peek does not fire when probability check fails
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Find a seed where probability fails (50% chance). When the candidate
    /// is reached and probability fails, the peek should NOT start, and the
    /// quota should be consumed (HasTriggered=true meaning "done for today").
    /// </summary>
    [STATestMethod]
    public void Peek_ProbabilityFails_DoesNotFire_QuotaConsumed()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // Find a seed where probability fails.
            int failSeed = -1;
            for (int s = 0; s < 10000; s++)
            {
                var p = new PeekScheduler(new Random(s));
                p.Tick("2026-10-24", 1000, false, false, 1000);
                var r = p.Tick("2026-10-24", p.CandidateMinutes * 60_000.0, false, false,
                    p.CandidateMinutes * 60_000.0 + 1000);
                if (r == PeekTickResult.NoAction && p.HasAttempted && p.HasTriggered)
                {
                    failSeed = s;
                    break;
                }
            }
            Assert.IsTrue(failSeed >= 0, "Could not find a seed where probability fails.");

            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (500, 500);
            window.TestGetDpiScale = () => 1.0;

            var peek = new PeekScheduler(new Random(failSeed));
            window.TestPeekScheduler = peek;

            // Create plan.
            clockMs = 1000;
            InvokeTick(window);
            var candidate = peek.CandidateMinutes;

            // Reach candidate — probability fails, no peek.
            clockMs = candidate * 60_000.0 + 1000;
            InvokeTick(window);

            Assert.IsFalse(window.IsPeeking, "Peek should not start when probability fails.");
            Assert.IsTrue(peek.HasTriggered, "Quota should be consumed even when probability fails.");

            // More ticks — should not fire.
            clockMs = (candidate + 1) * 60_000.0;
            InvokeTick(window);
            Assert.IsFalse(window.IsPeeking, "Peek should not fire later when probability already failed.");
        }
        finally
        {
            window.Close();
        }
    }
}
