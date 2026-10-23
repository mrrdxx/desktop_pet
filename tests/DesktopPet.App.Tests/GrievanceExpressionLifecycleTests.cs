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
            window.TestGetDpiScale = () => 1.0;

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
            Assert.AreEqual("curious", window.CurrentAction,
                $"Expression must play 'curious'. Got: {window.CurrentAction}");
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
            window.TestGetDpiScale = () => 1.0;

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
            Assert.AreEqual("curious", window.CurrentAction, "Should be playing curious.");

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
            window.TestGetDpiScale = () => 1.0;

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
            window.TestGetDpiScale = () => 1.0;

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
            Assert.AreEqual("idle", window.CurrentAction,
                $"Action must stay idle after expression completion. Got: {window.CurrentAction}");
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
            window.TestGetDpiScale = () => 1.0;

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
}
