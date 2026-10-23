using System.Reflection;
using DesktopPet.App;
using DesktopPet.Core.Emotion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// F-03: Real gesture chain tests for petting detection.
/// Drives the actual ProcessPettingMove entry point (the same logic used by
/// PetCanvas_MouseMove) with simulated mouse position sequences — NOT by
/// pre-setting _pettingAccumulatedMs.
///
/// Coverage:
/// - Stationary hold then single micro-move does NOT accumulate the hold time.
/// - Continuous effective micro-moves reaching 1s → +5 affinity via EndPetting.
/// - Moving out of head zone ends petting candidate.
/// - Large movement converts petting to drag (candidate cleared).
/// - Cooldown prevents repeat +5 within 2s window.
/// </summary>
[TestClass]
public class PettingGestureChainTests
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

    /// <summary>
    /// Helper: start a petting candidate at the given head-zone position and time.
    /// Resets cooldown to 0 by default; pass false to preserve existing cooldown.
    /// </summary>
    private static void StartPetting(MainWindow window, double x, double y, double clockMs, bool resetCooldown = true)
    {
        SetField(window, "_isPettingCandidate", true);
        SetField(window, "_pettingStartMs", clockMs);
        SetField(window, "_pettingAccumulatedMs", 0.0);
        SetField(window, "_pettingLastPos", new System.Windows.Point(x, y));
        if (resetCooldown)
            SetField(window, "_pettingCooldownUntilMs", 0.0);
    }

    // ── Stationary hold then micro-move: hold time NOT accumulated ─────

    /// <summary>
    /// F-03: Press and hold stationary for 2 seconds, then make a single
    /// 1-DIP micro-move. The 2 seconds of stationary time must NOT be
    /// accumulated — only the inter-event time of the effective move counts,
    /// and since the gap exceeds PettingMaxInterEventMs, even that is excluded.
    /// </summary>
    [STATestMethod]
    public void F03_StationaryHoldThenSingleMicroMove_DoesNotAccumulate()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;

            // Start petting at head center (60, 20).
            StartPetting(window, 60, 20, 0.0);

            // Hold stationary for 2 seconds (no ProcessPettingMove calls).
            clockMs = 2000.0;

            // Single micro-move of 1 DIP within head zone.
            var result = window.ProcessPettingMove(new System.Windows.Point(61, 20), clockMs);

            Assert.IsTrue(result, "ProcessPettingMove should return true for small move in head zone.");

            var accumulated = GetField<double>(window, "_pettingAccumulatedMs");
            // The inter-event gap is 2000ms > PettingMaxInterEventMs(500),
            // so the gap is NOT accumulated. accumulated should be 0.
            Assert.AreEqual(0.0, accumulated, 0.01,
                $"Stationary hold time must NOT be accumulated. Got {accumulated}ms.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Continuous micro-moves reaching 1s → +5 affinity ───────────────

    /// <summary>
    /// F-03: Continuous effective micro-moves (each 2 DIP, 50ms apart) that
    /// total >= 1000ms of accumulated time must trigger +5 affinity when
    /// EndPetting is called. This drives the real gesture chain, not pre-set
    /// accumulatedMs.
    /// </summary>
    [STATestMethod]
    public void F03_ContinuousMicroMoves_AccumulateToThreshold_TriggersPlus5()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;

            // Inject emotion accumulator for actual +5 assertion.
            var accumulator = new EmotionDecayAccumulator(50, 50, DateTime.UtcNow);
            window.TestEmotionAccumulator = accumulator;

            // Start petting at head center.
            StartPetting(window, 60, 20, 0.0);

            // Simulate 25 micro-moves, each 2 DIP horizontally, 50ms apart.
            // Total accumulated time: 25 * 50 = 1250ms > 1000ms threshold.
            for (int i = 1; i <= 25; i++)
            {
                clockMs = i * 50.0;
                var x = 60 + i * 2;
                // Keep within head zone (Y=20, well within upper 1/3 of 120px).
                var result = window.ProcessPettingMove(new System.Windows.Point(x, 20), clockMs);
                Assert.IsTrue(result, $"Move {i} should remain in petting candidate state.");
            }

            var accumulated = GetField<double>(window, "_pettingAccumulatedMs");
            Assert.IsTrue(accumulated >= 1000.0,
                $"Accumulated time must reach 1000ms. Got {accumulated}ms.");

            // EndPetting should trigger +5 affinity.
            var affinityBefore = accumulator.AffinityInt;
            Assert.AreEqual(50, affinityBefore, "Initial affinity should be 50.");

            InvokeMethod(window, "EndPetting");

            Assert.AreEqual(55, accumulator.AffinityInt,
                $"Affinity must be 55 (50+5) after petting threshold met. Got {accumulator.AffinityInt}.");

            // Cooldown should be set.
            var cooldown = GetField<double>(window, "_pettingCooldownUntilMs");
            Assert.AreEqual(clockMs + 2000.0, cooldown, 0.01,
                "Cooldown should be set to nowMs + 2000ms.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Moving out of head zone ends petting ───────────────────────────

    /// <summary>
    /// F-03: When the cursor moves from the head zone to the body zone
    /// (Y > 40 for 120px image), petting candidate must be ended.
    /// </summary>
    [STATestMethod]
    public void F03_MoveOutOfHeadZone_EndsPetting()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;

            var accumulator = new EmotionDecayAccumulator(50, 50, DateTime.UtcNow);
            window.TestEmotionAccumulator = accumulator;

            // Start petting at head center.
            StartPetting(window, 60, 20, 0.0);

            // A few micro-moves in head zone.
            for (int i = 1; i <= 5; i++)
            {
                clockMs = i * 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + i * 2, 20), clockMs);
            }

            Assert.IsTrue(window.IsPettingCandidate,
                "Should still be in petting candidate state within head zone.");

            // Move to body zone (Y=80, well below 1/3 boundary at Y=40).
            clockMs = 300.0;
            var result = window.ProcessPettingMove(new System.Windows.Point(70, 80), clockMs);

            Assert.IsFalse(result, "ProcessPettingMove should return false when leaving head zone.");
            Assert.IsFalse(window.IsPettingCandidate,
                "Petting candidate must be cleared when cursor leaves head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Large movement converts petting to drag ────────────────────────

    /// <summary>
    /// F-03: A movement exceeding PettingMaxMovePerFramePx (8 DIP) during
    /// petting candidate must end the candidate (convert to drag in real handler).
    /// </summary>
    [STATestMethod]
    public void F03_LargeMovement_EndsPettingCandidate()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;

            // Start petting at head center.
            StartPetting(window, 60, 20, 0.0);

            // Small moves first to establish petting.
            for (int i = 1; i <= 3; i++)
            {
                clockMs = i * 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + i * 2, 20), clockMs);
            }

            Assert.IsTrue(window.IsPettingCandidate, "Should be in petting candidate.");

            // Large movement: 15 DIP in one step (exceeds 8 DIP threshold).
            clockMs = 200.0;
            var result = window.ProcessPettingMove(new System.Windows.Point(80, 20), clockMs);

            Assert.IsFalse(result, "ProcessPettingMove should return false for large movement.");
            Assert.IsFalse(window.IsPettingCandidate,
                "Petting candidate must be cleared after large movement (drag conversion).");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Cooldown prevents repeat +5 ────────────────────────────────────

    /// <summary>
    /// F-03/F-05: After a successful petting (+5), a second petting within
    /// the 2-second cooldown window must NOT add another +5.
    /// </summary>
    [STATestMethod]
    public void F03_CooldownPreventsRepeatPlus5()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;

            var accumulator = new EmotionDecayAccumulator(50, 50, DateTime.UtcNow);
            window.TestEmotionAccumulator = accumulator;

            // First petting session: accumulate enough time.
            StartPetting(window, 60, 20, 0.0);
            for (int i = 1; i <= 25; i++)
            {
                clockMs = i * 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + i * 2, 20), clockMs);
            }
            InvokeMethod(window, "EndPetting");

            Assert.AreEqual(55, accumulator.AffinityInt,
                $"First petting should add +5. Got {accumulator.AffinityInt}.");

            // Second petting session within cooldown.
            clockMs += 100.0; // Only 100ms after first petting ended (within 2s cooldown).
            StartPetting(window, 60, 20, clockMs, resetCooldown: false);
            for (int i = 1; i <= 25; i++)
            {
                clockMs += 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + i * 2, 20), clockMs);
            }
            InvokeMethod(window, "EndPetting");

            Assert.AreEqual(55, accumulator.AffinityInt,
                $"Second petting within cooldown must NOT add +5. Got {accumulator.AffinityInt}.");

            // Third petting session after cooldown expires.
            // Cooldown was set at first petting end time + 2000ms.
            // Advance clock past cooldown.
            var cooldown = GetField<double>(window, "_pettingCooldownUntilMs");
            clockMs = cooldown + 100.0;
            StartPetting(window, 60, 20, clockMs, resetCooldown: false);
            for (int i = 1; i <= 25; i++)
            {
                clockMs += 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + i * 2, 20), clockMs);
            }
            InvokeMethod(window, "EndPetting");

            Assert.AreEqual(60, accumulator.AffinityInt,
                $"Third petting after cooldown should add +5. Got {accumulator.AffinityInt}.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Zero-displacement moves do not accumulate ──────────────────────

    /// <summary>
    /// F-03: Repeated ProcessPettingMove calls with the same position
    /// (zero displacement) must NOT accumulate any time, even if the
    /// clock advances. Per R-02 "按住未移动不算抚摸".
    /// </summary>
    [STATestMethod]
    public void F03_ZeroDisplacement_DoesNotAccumulate()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;

            // Start petting at head center.
            StartPetting(window, 60, 20, 0.0);

            // Send 20 zero-displacement "moves" (same position) over 1 second.
            for (int i = 1; i <= 20; i++)
            {
                clockMs = i * 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60, 20), clockMs);
            }

            var accumulated = GetField<double>(window, "_pettingAccumulatedMs");
            Assert.AreEqual(0.0, accumulated, 0.01,
                $"Zero-displacement moves must NOT accumulate. Got {accumulated}ms.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Long gap between effective moves: gap not accumulated ──────────

    /// <summary>
    /// F-03: Two effective micro-moves separated by a 1-second gap.
    /// The gap must NOT be accumulated (exceeds PettingMaxInterEventMs).
    /// Only the inter-event time of the second move (from the gap reset)
    /// is counted, which is also small.
    /// </summary>
    [STATestMethod]
    public void F03_LongGapBetweenMoves_GapNotAccumulated()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;

            // Start petting.
            StartPetting(window, 60, 20, 0.0);

            // First effective move at t=50ms.
            clockMs = 50.0;
            window.ProcessPettingMove(new System.Windows.Point(62, 20), clockMs);

            var accAfterFirst = GetField<double>(window, "_pettingAccumulatedMs");
            Assert.AreEqual(50.0, accAfterFirst, 0.01,
                $"First move should accumulate 50ms. Got {accAfterFirst}.");

            // Long gap: 1000ms with no moves, then a second effective move.
            // _pettingStartMs was set to 50ms by the first move.
            // The second move at t=1050ms has interEventMs = 1000ms > 500ms threshold.
            clockMs = 1050.0;
            window.ProcessPettingMove(new System.Windows.Point(64, 20), clockMs);

            var accAfterSecond = GetField<double>(window, "_pettingAccumulatedMs");
            // The 1000ms gap should NOT be accumulated. accumulated stays at 50ms.
            Assert.AreEqual(50.0, accAfterSecond, 0.01,
                $"Long gap must NOT be accumulated. Got {accAfterSecond}ms, expected 50ms.");

            // Now a third move shortly after (within threshold).
            clockMs = 1100.0; // 50ms after the second move.
            window.ProcessPettingMove(new System.Windows.Point(66, 20), clockMs);

            var accAfterThird = GetField<double>(window, "_pettingAccumulatedMs");
            // 50ms from second move should be accumulated: 50 + 50 = 100ms.
            Assert.AreEqual(100.0, accAfterThird, 0.01,
                $"Short gap after reset should accumulate. Got {accAfterThird}ms, expected 100ms.");
        }
        finally
        {
            window.Close();
        }
    }
}
