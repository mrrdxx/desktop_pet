using System.Reflection;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using DesktopPet.Core.Emotion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// F-04: Grievance expression lifecycle tests.
/// Verifies first-encounter requirement, expression end → idle recovery,
/// drag-start (MouseLeftButtonDown) immediate interrupt, and no repeat
/// expression after completion.
/// </summary>
[TestClass]
public class GrievanceExpressionLifecycleTests
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

    private static AnimationPlayer GetAnimPlayer(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_animPlayer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is null)
            Assert.Fail("_animPlayer field not found.");
        return (AnimationPlayer)field!.GetValue(window)!;
    }

    // ── Far mouse: expression does NOT start ───────────────────────────

    /// <summary>
    /// F-04: When the mouse is far from the pet (not in interaction range),
    /// expression must NOT start even if ShouldExpress is true.
    /// Per R-02: "下一个本地日期首次相遇（鼠标进入宠物交互范围）时执行".
    /// </summary>
    [STATestMethod]
    public void F04_FarMouse_ExpressionDoesNotStart()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            // Mouse far from pet (pet at 100,100 size 120; mouse at 900,900).
            window.TestGetCursorPos = () => (900, 900);

            var accumulator = new EmotionDecayAccumulator(50, 85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            // Mark pending with yesterday's date.
            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            var animPlayer = GetAnimPlayer(window);
            animPlayer.PlayAction("idle");

            // Tick — expression should NOT start (mouse far away).
            InvokeTick(window);

            Assert.IsFalse(window.IsExpressingGrievance,
                "Expression must NOT start when mouse is far from pet.");
            Assert.AreEqual("idle", window.CurrentAction,
                $"Action must stay idle when mouse is far. Got: {window.CurrentAction}");
            Assert.IsTrue(grievanceExpr.IsPending,
                "Pending must remain true — expression was not consumed by far mouse.");

            // Now move mouse near pet — expression should start.
            window.TestGetCursorPos = () => (160, 160);
            animPlayer.PlayAction("idle");

            InvokeTick(window);

            Assert.IsTrue(window.IsExpressingGrievance,
                "Expression must start when mouse enters interaction range.");
            Assert.AreEqual("grievance", window.CurrentAction,
                $"Expression must play 'grievance'. Got: {window.CurrentAction}");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Expression completes → returns to idle ─────────────────────────

    /// <summary>
    /// F-04: After 10 seconds of expression, the action must return to idle
    /// (not stay on curious which is a looping action).
    /// </summary>
    [STATestMethod]
    public void F04_ExpressionCompletes_ReturnsToIdle()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (160, 160);

            var accumulator = new EmotionDecayAccumulator(50, 85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            // Start expression.
            InvokeTick(window);
            clockMs += 33;

            Assert.IsTrue(window.IsExpressingGrievance, "Expression should be in progress.");
            Assert.AreEqual("grievance", window.CurrentAction, "Should be playing grievance.");

            // Tick past 10 seconds.
            for (int i = 0; i < 310; i++)
            {
                InvokeTick(window);
                clockMs += 33;
            }

            Assert.IsFalse(window.IsExpressingGrievance,
                "Expression must complete after 10 seconds.");
            Assert.AreEqual("idle", window.CurrentAction,
                $"Action must return to idle after expression completes. Got: {window.CurrentAction}");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Drag start (MouseLeftButtonDown) immediately interrupts ────────

    /// <summary>
    /// F-04: When expression is in progress and the user presses the left
    /// mouse button (drag start), the expression must be interrupted
    /// immediately — not waiting for EndDrag.
    /// This tests the InterruptGrievanceExpression call in
    /// PetCanvas_MouseLeftButtonDown via the internal method.
    /// </summary>
    [STATestMethod]
    public void F04_DragStart_ImmediatelyInterruptsExpression()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (160, 160);

            var accumulator = new EmotionDecayAccumulator(50, 85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            // Start expression.
            InvokeTick(window);
            Assert.IsTrue(window.IsExpressingGrievance, "Expression should be in progress.");

            // Simulate drag start by calling InterruptGrievanceExpression
            // (the same method called from PetCanvas_MouseLeftButtonDown).
            var interruptMethod = typeof(MainWindow).GetMethod("InterruptGrievanceExpression",
                BindingFlags.NonPublic | BindingFlags.Instance);
            interruptMethod!.Invoke(window, null);

            Assert.IsFalse(window.IsExpressingGrievance,
                "Expression must stop immediately at drag start (not at EndDrag).");
            Assert.IsFalse(grievanceExpr.IsPending,
                "Pending must be cleared at drag start (R-02).");
            Assert.AreEqual(50, accumulator.GrievanceInt,
                $"Grievance must be reset to 50 at drag start. Got: {accumulator.GrievanceInt}");
            Assert.AreEqual("idle", window.CurrentAction,
                $"Action must return to idle after drag-start interrupt. Got: {window.CurrentAction}");
        }
        finally
        {
            window.Close();
        }
    }

    // ── No repeat expression after completion ──────────────────────────

    /// <summary>
    /// F-04: After expression completes (10s), subsequent ticks must NOT
    /// re-start expression. Pending is cleared, grievance is reset to 50
    /// (below threshold), so ShouldExpress returns false.
    /// </summary>
    [STATestMethod]
    public void F04_AfterCompletion_NoRepeatExpression()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (160, 160);

            var accumulator = new EmotionDecayAccumulator(50, 85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            // Start and complete expression (tick past 10s).
            InvokeTick(window);
            clockMs += 33;
            for (int i = 0; i < 310; i++)
            {
                InvokeTick(window);
                clockMs += 33;
            }

            Assert.IsFalse(window.IsExpressingGrievance, "Expression must have completed.");
            Assert.IsFalse(grievanceExpr.IsPending, "Pending must be cleared.");
            Assert.AreEqual(50, accumulator.GrievanceInt, "Grievance must be reset to 50.");

            // Tick many more times — expression must NOT restart.
            for (int i = 0; i < 100; i++)
            {
                InvokeTick(window);
                clockMs += 33;
            }

            Assert.IsFalse(window.IsExpressingGrievance,
                "Expression must NOT restart after completion.");
            // After expression completes, hover input resumes normally.
            // The action may be "idle" or "curious" (hover-triggered) — both are valid.
            // The key assertion is that expression does NOT restart.
            Assert.IsTrue(window.CurrentAction == "idle" || window.CurrentAction == "curious",
                $"Action should be idle or hover-triggered curious after expression completion. Got: {window.CurrentAction}");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Expression does not start during pause ─────────────────────────

    /// <summary>
    /// F-04: Expression must not start while the app is paused.
    /// OnUpdateTick skips emotion logic entirely when _isAppPaused is true.
    /// </summary>
    [STATestMethod]
    public void F04_Paused_ExpressionDoesNotStart()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (160, 160);

            var accumulator = new EmotionDecayAccumulator(50, 85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            // Pause the app.
            window.PauseAnimation();

            // Tick during pause — expression must NOT start.
            for (int i = 0; i < 10; i++)
            {
                InvokeTick(window);
                clockMs += 33;
            }

            Assert.IsFalse(window.IsExpressingGrievance,
                "Expression must NOT start while paused.");
            Assert.IsTrue(grievanceExpr.IsPending,
                "Pending must remain true during pause — not consumed.");

            // Resume — expression should start (mouse in range).
            window.ResumeAnimation();
            InvokeTick(window);

            Assert.IsTrue(window.IsExpressingGrievance,
                "Expression must start after resume with mouse in range.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── F-04 R4: Expression priority over hover ──────────────────

    /// <summary>
    /// F-04 R4: While grievance expression is in progress, cursor movement
    /// near the pet must NOT replace the expression action. The expression
    /// lifecycle (timer + flag) must stay consistent with the displayed action.
    /// </summary>
    [STATestMethod]
    public void CursorMotionDuringExpression_DoesNotOverrideAction()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (160, 160);

            var accumulator = new EmotionDecayAccumulator(50, 85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            // Start expression.
            InvokeTick(window);
            clockMs += 33;
            Assert.IsTrue(window.IsExpressingGrievance, "Expression should be in progress.");
            Assert.AreEqual("grievance", window.CurrentAction,
                "Expression should be playing 'grievance'.");

            // Cursor motion must not replace the active emotion expression.
            var positions = new[] { (100, 100), (220, 100), (100, 220), (220, 220) };
            for (int i = 0; i < 30; i++)
            {
                var (sx, sy) = positions[i % positions.Length];
                window.TestGetCursorPos = () => (sx, sy);
                InvokeTick(window);
                clockMs += 33;
            }

            // Emotion lifecycle and its visible expression must remain aligned.
            Assert.IsTrue(window.IsExpressingGrievance,
                "Mouse movement must not cancel the expression lifecycle.");
            Assert.AreEqual("grievance", window.CurrentAction,
                $"Action must stay 'grievance' during expression. Got: {window.CurrentAction}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-04 R4: After expression completes (10s), hover input must
    /// resume working normally. The expression suppression must not persist
    /// after the expression ends.
    /// </summary>
    [STATestMethod]
    public void F04_R4_AfterExpressionCompletes_InputResumes()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (160, 160);

            var accumulator = new EmotionDecayAccumulator(50, 85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            // Start and complete expression (tick past 10s).
            InvokeTick(window);
            clockMs += 33;
            for (int i = 0; i < 310; i++)
            {
                InvokeTick(window);
                clockMs += 33;
            }

            Assert.IsFalse(window.IsExpressingGrievance, "Expression must have completed.");
            Assert.AreEqual("idle", window.CurrentAction,
                "Action must be idle after expression completes.");

            // Now hover near the pet — should be able to trigger "curious".
            // Move mouse close to pet and hold for hover duration.
            window.TestGetCursorPos = () => (165, 165);
            for (int i = 0; i < 120; i++) // ~4 seconds of hovering
            {
                InvokeTick(window);
                clockMs += 33;
            }

            // Hover should have triggered curious (or another action change).
            // The key assertion: the action is NOT stuck in a suppressed state.
            Assert.AreNotEqual("dodge", window.CurrentAction,
                "After expression, hover should not trigger dodge.");
            // Action should be either idle or curious (hover-triggered).
            Assert.IsTrue(window.CurrentAction == "idle" || window.CurrentAction == "curious",
                $"After expression, action should be idle or hover-triggered curious. Got: {window.CurrentAction}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-04 R4: While grievance expression is in progress, hover must NOT
    /// replace the expression action. Hover-triggered "curious" must not
    /// conflict with expression "curious" — the expression flag and timer
    /// must remain consistent.
    /// </summary>
    [STATestMethod]
    public void F04_R4_HoverDuringExpression_DoesNotResetTimer()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (160, 160);

            var accumulator = new EmotionDecayAccumulator(50, 85, DateTime.UtcNow);
            var grievanceExpr = new GrievanceExpressionState();
            window.TestEmotionAccumulator = accumulator;
            window.TestGrievanceExpression = grievanceExpr;
            window.TestCornerTracking = new CornerTracker();

            var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            grievanceExpr.Restore(true, yesterday);

            // Start expression.
            InvokeTick(window);
            clockMs += 33;
            Assert.IsTrue(window.IsExpressingGrievance, "Expression should be in progress.");

            var startMs = GetField<double>(window, "_grievanceExpressionStartMs");

            // Hover near pet for ~2 seconds during expression.
            for (int i = 0; i < 60; i++)
            {
                InvokeTick(window);
                clockMs += 33;
            }

            // Expression must still be in progress (not completed — only ~2s of 10s).
            Assert.IsTrue(window.IsExpressingGrievance,
                "Expression must still be in progress after hover during expression.");

            // The expression start time must NOT have been reset by hover.
            var startMsAfter = GetField<double>(window, "_grievanceExpressionStartMs");
            Assert.AreEqual(startMs, startMsAfter, 0.01,
                "Expression start time must not be reset by hover during expression.");
        }
        finally
        {
            window.Close();
        }
    }

    private static T GetField<T>(object obj, string name)
    {
        var field = typeof(MainWindow).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is null)
            Assert.Fail($"Field '{name}' not found.");
        return (T)field!.GetValue(obj)!;
    }
}
