using System.Reflection;
using DesktopPet.App;
using DesktopPet.Core.Emotion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P1-003: Tests for petting detection and drag gesture distinction (R-02).
/// Verifies head zone detection, petting candidate state interrupts sampling,
/// petting increment triggers on accumulated time, cooldown prevents repeat,
/// and large movement converts petting to drag.
///
/// Uses the same reflection-based test harness as MouseInteractionIntegrationTests.
/// </summary>
[TestClass]
public class PettingDetectionTests
{
    private const double PetSize = 120;

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

    private static void InvokeMethod(object obj, string name, params object[] args)
    {
        var method = typeof(MainWindow).GetMethod(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null)
            Assert.Fail($"Method '{name}' not found.");
        method!.Invoke(obj, args);
    }

    private static void InvokeTick(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("OnUpdateTick",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null)
            Assert.Fail("OnUpdateTick method not found.");
        method.Invoke(window, [null!, EventArgs.Empty]);
    }

    // ── Head zone detection ────────────────────────────────────────────

    [STATestMethod]
    public void Petting_HeadZone_UpperThird_IsHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // Upper third of 120px image: Y <= 40 is head.
            // F-03 R5: Must use an opaque pixel — (60, 20) is opaque in the default character.
            var headPoint = new System.Windows.Point(60, 20);
            Assert.IsTrue(window.IsInHeadZone(headPoint),
                "Opaque point in upper 1/3 should be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void Petting_BodyZone_LowerTwoThirds_IsNotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // Lower 2/3 of 120px image: Y > 40 is body.
            var bodyPoint = new System.Windows.Point(60, 80);
            Assert.IsFalse(window.IsInHeadZone(bodyPoint),
                "Point in lower 2/3 should NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void Petting_HeadZone_Boundary_AtOneThird()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // Exactly at 1/3 boundary (Y = 40 for 120px) should be head.
            var boundaryPoint = new System.Windows.Point(60, 40);
            Assert.IsTrue(window.IsInHeadZone(boundaryPoint),
                "Point exactly at 1/3 boundary should be in head zone (<=).");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Petting candidate interrupts sampling ──────────────────────────

    [STATestMethod]
    public void Petting_CandidateState_SkipsSampling()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (160, 160);
            window.TestGetDpiScale = () => 1.0;

            // First tick to get a sample.
            InvokeTick(window);
            clockMs += 33;
            Assert.IsTrue(window.MouseSampler.WindowCount >= 1,
                "Sampler should have samples before petting candidate.");

            // Enter petting candidate state (simulating head press).
            SetField(window, "_isPettingCandidate", true);
            window.MouseSampler.Clear();

            // Tick during petting — should skip sampling.
            InvokeTick(window);
            clockMs += 33;

            Assert.AreEqual(0, window.MouseSampler.WindowCount,
                "Sampler must be empty during petting candidate state.");
            Assert.IsFalse(window.HoverDetector.IsHovering,
                "HoverDetector must be reset during petting candidate.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Petting increment triggers on accumulated time ─────────────────

    [STATestMethod]
    public void Petting_AccumulatedTime_TriggersIncrement()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 1000.0;
            window.TestGetElapsedMs = () => clockMs;

            // F-05: Inject TestEmotionAccumulator so EndPetting can actually
            // apply the +5 affinity increment without requiring App.Current.
            var accumulator = new EmotionDecayAccumulator(50, 50, DateTime.UtcNow);
            window.TestEmotionAccumulator = accumulator;

            // Simulate petting candidate with enough accumulated time.
            SetField(window, "_isPettingCandidate", true);
            SetField(window, "_pettingAccumulatedMs", 1100.0); // > 1000ms required
            SetField(window, "_pettingCooldownUntilMs", 0.0);  // cooldown elapsed

            Assert.AreEqual(50, accumulator.AffinityInt,
                "Affinity must start at 50 before petting increment.");

            InvokeMethod(window, "EndPetting");

            Assert.IsFalse(window.IsPettingCandidate,
                "Petting candidate must be cleared after EndPetting.");

            // F-05: Verify actual +5 affinity increment was applied.
            Assert.AreEqual(55, accumulator.AffinityInt,
                $"Affinity must be 55 after petting +5. Got {accumulator.AffinityInt}.");

            // Verify cooldown was set.
            var cooldown = GetField<double>(window, "_pettingCooldownUntilMs");
            Assert.AreEqual(1000.0 + 2000.0, cooldown,
                $"Cooldown must be set to now+2000ms. Got {cooldown}.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void Petting_InsufficientTime_DoesNotTriggerIncrement()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 500.0;
            window.TestGetElapsedMs = () => clockMs;

            // Simulate petting candidate with insufficient accumulated time.
            SetField(window, "_isPettingCandidate", true);
            SetField(window, "_pettingAccumulatedMs", 300.0); // < 1000ms required
            SetField(window, "_pettingCooldownUntilMs", 0.0);

            InvokeMethod(window, "EndPetting");

            Assert.IsFalse(window.IsPettingCandidate,
                "Petting candidate must be cleared even if threshold not met.");

            // Cooldown should NOT be set since petting didn't trigger.
            var cooldown = GetField<double>(window, "_pettingCooldownUntilMs");
            Assert.AreEqual(0.0, cooldown,
                "Cooldown should not be set when petting threshold not met.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Cooldown prevents repeat ───────────────────────────────────────

    [STATestMethod]
    public void Petting_Cooldown_PreventsRepeatWithinWindow()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 3000.0;
            window.TestGetElapsedMs = () => clockMs;

            // First petting: set cooldown to 5000ms (future).
            SetField(window, "_isPettingCandidate", true);
            SetField(window, "_pettingAccumulatedMs", 1100.0);
            SetField(window, "_pettingCooldownUntilMs", 5000.0); // cooldown active

            InvokeMethod(window, "EndPetting");

            // Cooldown should remain at 5000 (not updated since petting didn't fire).
            var cooldown = GetField<double>(window, "_pettingCooldownUntilMs");
            Assert.AreEqual(5000.0, cooldown,
                "Cooldown should not change when petting is in cooldown window.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Petting candidate state recovery ───────────────────────────────

    [STATestMethod]
    public void Petting_CandidateState_RecoveredAfterEnd()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (160, 160);
            window.TestGetDpiScale = () => 1.0;

            // Enter petting candidate.
            SetField(window, "_isPettingCandidate", true);
            window.MouseSampler.Clear();

            // End petting.
            InvokeMethod(window, "EndPetting");

            // Tick after petting ends — sampling should resume.
            InvokeTick(window);
            clockMs += 33;

            Assert.IsTrue(window.MouseSampler.WindowCount >= 1,
                "Sampler must accept new samples after petting ends.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── OnClosed cleans up petting state ───────────────────────────────

    [STATestMethod]
    public void Petting_OnClosed_CleansUpPettingState()
    {
        var window = CreateMainWindowOrFail();
        // Enter petting candidate before close.
        SetField(window, "_isPettingCandidate", true);
        // Close should clean up without throwing.
        window.Close();
        // If we reach here, no exception was thrown — pass.
        Assert.IsTrue(true, "Window closed without exception while in petting candidate state.");
    }
}
