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
    /// F-03: Continuous effective micro-moves (each 1 DIP, 50ms apart) that
    /// total >= 1000ms of accumulated time must trigger +5 affinity when
    /// EndPetting is called. This drives the real gesture chain, not pre-set
    /// accumulatedMs.
    /// F-03 R5: Moves stay on opaque pixels (y=30, x ∈ [60, 75] pingpong)
    /// to ensure the Alpha check in IsInHeadZone does not reject them.
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

            // Start petting at an opaque head pixel (60, 30).
            StartPetting(window, 60, 30, 0.0);

            // Simulate 25 micro-moves, each 1 DIP horizontally, 50ms apart.
            // Pingpong between x=60 and x=67 to stay within opaque pixels
            // at y=30 (opaque range covers x=55-79 at y>=23).
            // Max step distance = 7 DIP < PettingMaxMovePerFramePx (8).
            // Total accumulated time: 25 * 50 = 1250ms > 1000ms threshold.
            for (int i = 1; i <= 25; i++)
            {
                clockMs = i * 50.0;
                var x = 60 + (i % 8); // pingpong 60..67, max step 7 DIP
                var result = window.ProcessPettingMove(new System.Windows.Point(x, 30), clockMs);
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
    /// F-03 R5: Uses y=30 for head moves (opaque) and y=80 for body (opaque, below head).
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

            // Start petting at an opaque head pixel (60, 30).
            StartPetting(window, 60, 30, 0.0);

            // A few micro-moves in head zone (y=30, opaque).
            for (int i = 1; i <= 5; i++)
            {
                clockMs = i * 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + (i % 4), 30), clockMs);
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
    /// F-03 R5: Uses y=30 for head moves (opaque).
    /// </summary>
    [STATestMethod]
    public void F03_LargeMovement_EndsPettingCandidate()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;

            // Start petting at an opaque head pixel (60, 30).
            StartPetting(window, 60, 30, 0.0);

            // Small moves first to establish petting.
            for (int i = 1; i <= 3; i++)
            {
                clockMs = i * 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + (i % 4), 30), clockMs);
            }

            Assert.IsTrue(window.IsPettingCandidate, "Should be in petting candidate.");

            // Large movement: 15 DIP in one step (exceeds 8 DIP threshold).
            clockMs = 200.0;
            var result = window.ProcessPettingMove(new System.Windows.Point(78, 30), clockMs);

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
    /// F-03 R5: Uses y=30 with 1 DIP pingpong moves to stay on opaque pixels.
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
            StartPetting(window, 60, 30, 0.0);
            for (int i = 1; i <= 25; i++)
            {
                clockMs = i * 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + (i % 8), 30), clockMs);
            }
            InvokeMethod(window, "EndPetting");

            Assert.AreEqual(55, accumulator.AffinityInt,
                $"First petting should add +5. Got {accumulator.AffinityInt}.");

            // Second petting session within cooldown.
            clockMs += 100.0; // Only 100ms after first petting ended (within 2s cooldown).
            StartPetting(window, 60, 30, clockMs, resetCooldown: false);
            for (int i = 1; i <= 25; i++)
            {
                clockMs += 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + (i % 8), 30), clockMs);
            }
            InvokeMethod(window, "EndPetting");

            Assert.AreEqual(55, accumulator.AffinityInt,
                $"Second petting within cooldown must NOT add +5. Got {accumulator.AffinityInt}.");

            // Third petting session after cooldown expires.
            // Cooldown was set at first petting end time + 2000ms.
            // Advance clock past cooldown.
            var cooldown = GetField<double>(window, "_pettingCooldownUntilMs");
            clockMs = cooldown + 100.0;
            StartPetting(window, 60, 30, clockMs, resetCooldown: false);
            for (int i = 1; i <= 25; i++)
            {
                clockMs += 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + (i % 8), 30), clockMs);
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
    /// F-03 R5: Uses y=30 (opaque pixel).
    /// </summary>
    [STATestMethod]
    public void F03_ZeroDisplacement_DoesNotAccumulate()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;

            // Start petting at an opaque head pixel (60, 30).
            StartPetting(window, 60, 30, 0.0);

            // Send 20 zero-displacement "moves" (same position) over 1 second.
            for (int i = 1; i <= 20; i++)
            {
                clockMs = i * 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60, 30), clockMs);
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
    /// F-03 R5: Uses y=30 (opaque pixel).
    /// </summary>
    [STATestMethod]
    public void F03_LongGapBetweenMoves_GapNotAccumulated()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;

            // Start petting at an opaque head pixel (60, 30).
            StartPetting(window, 60, 30, 0.0);

            // First effective move at t=50ms.
            clockMs = 50.0;
            window.ProcessPettingMove(new System.Windows.Point(62, 30), clockMs);

            var accAfterFirst = GetField<double>(window, "_pettingAccumulatedMs");
            Assert.AreEqual(50.0, accAfterFirst, 0.01,
                $"First move should accumulate 50ms. Got {accAfterFirst}.");

            // Long gap: 1000ms with no moves, then a second effective move.
            // _pettingStartMs was set to 50ms by the first move.
            // The second move at t=1050ms has interEventMs = 1000ms > 500ms threshold.
            clockMs = 1050.0;
            window.ProcessPettingMove(new System.Windows.Point(64, 30), clockMs);

            var accAfterSecond = GetField<double>(window, "_pettingAccumulatedMs");
            // The 1000ms gap should NOT be accumulated. accumulated stays at 50ms.
            Assert.AreEqual(50.0, accAfterSecond, 0.01,
                $"Long gap must NOT be accumulated. Got {accAfterSecond}ms, expected 50ms.");

            // Now a third move shortly after (within threshold).
            clockMs = 1100.0; // 50ms after the second move.
            window.ProcessPettingMove(new System.Windows.Point(66, 30), clockMs);

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

    // ── Head zone boundary: X/Y bounds ────────────────────────────────

    /// <summary>
    /// F-03 R4: IsInHeadZone must reject points with X beyond image width.
    /// A point at (200, 20) with a 120px image is outside the head zone.
    /// </summary>
    [STATestMethod]
    public void F03_HeadZone_XBeyondWidth_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // X=200 exceeds 120px width.
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(200, 20)),
                "X beyond image width must NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-03 R4: IsInHeadZone must reject points with negative X.
    /// </summary>
    [STATestMethod]
    public void F03_HeadZone_NegativeX_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(-5, 20)),
                "Negative X must NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-03 R4: IsInHeadZone must reject points with negative Y.
    /// A point at (60, -10) is above the image, not in the head zone.
    /// </summary>
    [STATestMethod]
    public void F03_HeadZone_NegativeY_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(60, -10)),
                "Negative Y must NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-03 R4: ProcessPettingMove with a point that has moved laterally
    /// beyond the image width must end petting (return false), not continue
    /// accumulating.
    /// </summary>
    [STATestMethod]
    public void F03_LateralMoveBeyondImage_EndsPetting()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;

            StartPetting(window, 60, 20, 0.0);

            // A few valid micro-moves in head zone.
            for (int i = 1; i <= 5; i++)
            {
                clockMs = i * 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + i * 2, 20), clockMs);
            }

            Assert.IsTrue(window.IsPettingCandidate, "Should be in petting candidate within head zone.");

            // Move laterally beyond image width (X=130 > 120).
            clockMs = 300.0;
            var result = window.ProcessPettingMove(new System.Windows.Point(130, 20), clockMs);

            // X=130 exceeds image width — IsInHeadZone returns false → EndPetting.
            // Also moveDist from last pos (70→130=60) exceeds threshold → candidate cleared.
            Assert.IsFalse(result, "ProcessPettingMove should return false when X exceeds image width.");
            Assert.IsFalse(window.IsPettingCandidate,
                "Petting candidate must be cleared when cursor moves beyond image boundary.");

            var accumulated = GetField<double>(window, "_pettingAccumulatedMs");
            // The accumulated time from the first 5 moves should NOT have increased
            // by the lateral-beyond-boundary move.
            // (The move either ended petting via IsInHeadZone=false or via large moveDist.)
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-03 R4/R5: IsInHeadZone at X=0 (left image boundary) returns false because
    /// the point is on a transparent pixel, even though it is within the head
    /// rectangle. The rectangular boundary is inclusive, but the Alpha check
    /// (R5) requires a visible pixel.
    /// </summary>
    [STATestMethod]
    public void F03_HeadZone_XAtZero_Transparent_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // X=0 is within the image rectangle but on a transparent pixel
            // (opaque bounds start at X≈30 for the default character).
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(0, 20)),
                "X=0 is in the head rectangle but on a transparent pixel — must NOT be head zone (R5 Alpha check).");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-03 R4/R5: IsInHeadZone at X=width (right image boundary) returns false
    /// because the point is beyond the opaque pixel area.
    /// </summary>
    [STATestMethod]
    public void F03_HeadZone_XAtWidth_Transparent_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // X=120 = image width, but on a transparent pixel.
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(120, 20)),
                "X=width is in the head rectangle but on a transparent pixel — must NOT be head zone (R5 Alpha check).");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-03 R5: A point on a visible (opaque) pixel within the head rectangle
    /// IS in the head zone. This confirms the Alpha check does not reject
    /// valid head pixels.
    /// </summary>
    [STATestMethod]
    public void F03_HeadZone_OpaquePixelInHead_IsHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // (60, 20) is an opaque pixel in the upper 1/3 of the default character.
            Assert.IsTrue(window.IsInHeadZone(new System.Windows.Point(60, 20)),
                "Opaque pixel within head rectangle must be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── F-03 R4: Transparent area does not continue accumulation ──────

    /// <summary>
    /// F-03 R4: Moving to a point within the image rectangle but in the
    /// transparent/empty area (below head zone, i.e. body zone) must end
    /// petting. This is already covered by MoveOutOfHeadZone, but this test
    /// specifically verifies that the accumulated time does NOT increase
    /// when the cursor enters the transparent area — the petting ends
    /// immediately without adding the transition time.
    /// </summary>
    [STATestMethod]
    public void F03_R4_TransparentArea_DoesNotContinueAccumulating()
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

            // A few valid micro-moves in head zone to accumulate some time.
            for (int i = 1; i <= 5; i++)
            {
                clockMs = i * 50.0;
                window.ProcessPettingMove(new System.Windows.Point(60 + i * 2, 20), clockMs);
            }

            var accumulatedBefore = GetField<double>(window, "_pettingAccumulatedMs");
            Assert.IsTrue(accumulatedBefore > 0,
                $"Should have accumulated some time from valid head-zone moves. Got {accumulatedBefore}ms.");

            // Move to transparent area (Y=80, well below head zone boundary at Y=40).
            // This is within the image rectangle but in the body/transparent zone.
            clockMs = 300.0;
            var result = window.ProcessPettingMove(new System.Windows.Point(70, 80), clockMs);

            Assert.IsFalse(result, "ProcessPettingMove should return false when entering transparent/body area.");
            Assert.IsFalse(window.IsPettingCandidate,
                "Petting candidate must be cleared when cursor enters transparent area.");

            // The accumulated time must NOT have increased by the transition.
            var accumulatedAfter = GetField<double>(window, "_pettingAccumulatedMs");
            Assert.AreEqual(accumulatedBefore, accumulatedAfter, 0.01,
                $"Accumulated time must NOT increase when moving to transparent area. " +
                $"Before: {accumulatedBefore}ms, After: {accumulatedAfter}ms.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-03 R4: IsInHeadZone must reject Y=0 at negative X (combined boundary).
    /// A point at (-1, 0) is outside the image, not in the head zone.
    /// </summary>
    [STATestMethod]
    public void F03_R4_NegativeXZeroY_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(-1, 0)),
                "Negative X with Y=0 must NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-03 R4: IsInHeadZone must reject a point with very large X and small Y.
    /// R4 noted "任意大的 X 且 Y=20，仍返回 true" — this must be fixed.
    /// </summary>
    [STATestMethod]
    public void F03_R4_VeryLargeX_SmallY_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(10000, 20)),
                "Very large X with small Y must NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── F-03 R5: Transparent pixel within head rectangle ─────────────

    /// <summary>
    /// F-03 R5: A point inside the head rectangle (upper 1/3) but on a
    /// transparent pixel must NOT be considered head zone. The default
    /// character's opaque bounds start at approximately (30, 13), so
    /// (60, 5) is within the head rectangle (Y=5 < 40) but fully transparent.
    /// An adjacent point (60, 20) is on an opaque pixel and IS head zone.
    /// This test verifies the contrast: transparent → false, adjacent opaque → true.
    /// </summary>
    [STATestMethod]
    public void F03_R5_TransparentInHeadRect_NotHead_AdjacentOpaque_IsHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // (60, 5) is in the head rectangle (Y=5 < 40) but transparent.
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(60, 5)),
                "Transparent pixel within head rectangle must NOT be head zone (R5).");

            // (60, 20) is in the head rectangle AND on an opaque pixel.
            Assert.IsTrue(window.IsInHeadZone(new System.Windows.Point(60, 20)),
                "Adjacent opaque pixel within head rectangle must be head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-03 R5: When the cursor moves from an opaque head pixel to a transparent
    /// area within the head rectangle (e.g. above the character's head), petting
    /// must end and accumulated time must NOT increase. This verifies that the
    /// Alpha check in IsInHeadZone prevents continued accumulation on transparent
    /// space within the head rectangle — the specific gap R5 identified.
    /// </summary>
    [STATestMethod]
    public void F03_R5_TransparentInHeadRect_EndsPetting_NoAccumulation()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            double clockMs = 0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestEmotionAccumulator = new EmotionDecayAccumulator(50, 50, DateTime.UtcNow);
            var image = (System.Windows.Controls.Image)window.FindName("PetImage");
            var bitmap = (System.Windows.Media.Imaging.BitmapSource)image.Source;
            var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth;
            var pixels = new byte[width * bgra.PixelHeight * 4];
            bgra.CopyPixels(pixels, width * 4, 0);
            // Derive a real adjacent opaque/transparent pair from the current
            // PNG, independently of production hit testing. A hard-coded point
            // from the former prototype can become opaque in a new character.
            int bx = -1, by = -1;
            for (int y = 1; y < bgra.PixelHeight / 3 && bx < 0; y++)
            for (int x = 20; x < width - 21; x++)
            {
                if (pixels[(y * width + x) * 4 + 3] > 0
                    && pixels[(y * width + x + 1) * 4 + 3] > 0
                    && pixels[((y - 1) * width + x) * 4 + 3] == 0)
                { bx = x; by = y; break; }
            }
            Assert.IsTrue(bx >= 0, "A real opaque head boundary must exist.");
            StartPetting(window, bx + 0.5, by + 0.5, 0);
            for (int i = 1; i <= 5; i++)
            {
                clockMs = i * 50;
                window.ProcessPettingMove(new System.Windows.Point(bx + (i % 2) + 0.5, by + 0.5), clockMs);
            }
            Assert.IsTrue(window.IsPettingCandidate);
            double before = GetField<double>(window, "_pettingAccumulatedMs");
            Assert.IsTrue(before > 0);
            clockMs = 300;
            var result = window.ProcessPettingMove(new System.Windows.Point(bx + 0.5, by - 0.5), clockMs);
            Assert.IsFalse(result, "A sub-2px move onto a transparent head pixel must end the gesture.");
            Assert.IsFalse(window.IsPettingCandidate);
            Assert.AreEqual(before, GetField<double>(window, "_pettingAccumulatedMs"), 0.001);
            Assert.AreEqual(50, window.TestEmotionAccumulator.AffinityInt, "An incomplete gesture must not award affection.");
        }
        finally { window.Close(); }
    }
}